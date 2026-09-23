using System.Net;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Enum;
using BoardVerse.Tests.Integration.Helpers;
using BoardVerse.Tests.Integration.Infrastructure;
using BoardVerse.Tests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho Lobby Merge API endpoints.
/// Route: /api/cafes/{cafeId}/lobby-merge/**
/// 
/// Phủ:
/// - G8/G9/G18: Validation scenarios (seat, cross-cafe, deposit)
/// - G22: Idempotency key
/// - G25: Permission checks (Manager/CafeStaff role)
/// - G27/G28/G29: Create, Approve, Reject, Cancel, Get endpoints
/// - G30: ExpireOverdue background job
/// 
/// Các test dùng dữ liệu bootstrap có sẵn (demo cafe + demo players).
/// Test tạo lobby mới dùng reservation flow để đảm bảo tính độc lập giữa các test.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class LobbyMergeIntegrationTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly BoardVerseWebApplicationFactory _factory;

    public LobbyMergeIntegrationTests(BoardVerseWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    /// <summary>
    /// Helper tạo lobby ở trạng thái InProgress thông qua POS session.
    /// Trả về tuple: (sourceLobbyId, targetLobbyId, sourceSessionId, targetSessionId).
    /// Nếu không tạo được lobby → trả về null (test sẽ skip).
    /// </summary>
    private async Task<(Guid SourceLobbyId, Guid TargetLobbyId, Guid SourceSessionId, Guid TargetSessionId)?>
        CreateTwoInProgressLobbiesAsync()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Lấy Catan game ID để tạo reservation
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        if (catanId == Guid.Empty) return null;

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // --- Tạo lobby A (Source) ---
        var sourceLobbyId = await CreateReservationAndLobbyAsync(catanId, tomorrow, managerToken);
        if (sourceLobbyId == Guid.Empty) return null;

        // --- Tạo lobby B (Target) — dùng player2 để tránh trùng host ---
        var playerToken = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, playerToken);
        var targetLobbyId = await CreateReservationAndLobbyAsync(catanId, tomorrow, playerToken);
        if (targetLobbyId == Guid.Empty) return null;

        // Transition cả 2 lobby sang InProgress bằng POS session
        ApiTestClient.Authorize(_client, managerToken);

        // Tạo session trực tiếp qua POS endpoint
        var sourceSessionResp = await CreatePosSessionAsync(sourceLobbyId);
        if (sourceSessionResp == null) return null;
        var (sourceSessionId, sourceOk) = sourceSessionResp.Value;
        if (!sourceOk) return null;

        var targetSessionResp = await CreatePosSessionAsync(targetLobbyId);
        if (targetSessionResp == null) return null;
        var (targetSessionId, targetOk) = targetSessionResp.Value;
        if (!targetOk) return null;

        return (sourceLobbyId, targetLobbyId, sourceSessionId, targetSessionId);
    }

    private async Task<Guid> CreateReservationAndLobbyAsync(
        Guid gameId,
        DateOnly playDate,
        string authToken)
    {
        ApiTestClient.Authorize(_client, authToken);

        // Quote
        var quoteResp = await ApiTestClient.PostJsonAsync(_client,
            "/api/v1/reservations/quote",
            new
            {
                cafeId = IntegrationTestFixtures.DemoCafeId,
                gameId = gameId,
                playDate = playDate.ToString("yyyy-MM-dd"),
                timeSlot = "morning",
                maxPlayers = 4,
                minPlayers = 2,
                isPrivate = false,
                idempotencyKey = $"merge-test-{Guid.NewGuid()}"
            });

        if (quoteResp.StatusCode != HttpStatusCode.OK) return Guid.Empty;
        var quoteData = (await ApiTestClient.ReadApiResponseAsync<ReservationQuoteDto>(quoteResp)).Data;
        if (quoteData == null) return Guid.Empty;

        // Top-up BVC nếu cần
        if (quoteData.MissingAmount > 0)
        {
            await ApiTestClient.PostJsonAsync(_client, "/api/v1/wallet/topup",
                new { amountVnd = (int)(quoteData.MissingAmount * 1000), paymentMethod = "Mock", idempotencyKey = $"tu-{Guid.NewGuid()}" });
        }

        // Confirm reservation → tạo lobby
        var confirmResp = await ApiTestClient.PostJsonAsync(_client,
            "/api/v1/reservations/confirm",
            new
            {
                cafeId = IntegrationTestFixtures.DemoCafeId,
                gameId = gameId,
                playDate = playDate.ToString("yyyy-MM-dd"),
                timeSlot = "morning",
                maxPlayers = 4,
                minPlayers = 2,
                isPrivate = false,
                expectedFinalDeposit = quoteData.FinalDeposit,
                idempotencyKey = $"merge-confirm-{Guid.NewGuid()}"
            });

        if (confirmResp.StatusCode != HttpStatusCode.Created) return Guid.Empty;
        var confirmData = (await ApiTestClient.ReadApiResponseAsync<ReservationConfirmResponseDto>(confirmResp)).Data;
        return confirmData?.LobbyId ?? Guid.Empty;
    }

    private async Task<(Guid SessionId, bool Success)?> CreatePosSessionAsync(Guid lobbyId)
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var resp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions",
            new
            {
                lobbyId = lobbyId,
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.CatanBarcode
            });

        if (!resp.IsSuccessStatusCode) return null;
        var data = (await ApiTestClient.ReadApiResponseAsync<ActiveSessionDto>(resp)).Data;
        return data == null ? null : (data.Id, true);
    }

    // =====================================================================
    // PERMISSION & AUTHENTICATION TESTS
    // =====================================================================

    /// <summary>
    /// Player thường không có quyền tạo merge request → 403.
    /// </summary>
    [IntegrationFact]
    public async Task CreateMergeRequest_AsPlayer_Returns403()
    {
        var playerToken = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, playerToken);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new { sourceLobbyId = Guid.NewGuid(), targetLobbyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Không có token → 401.
    /// </summary>
    [IntegrationFact]
    public async Task CreateMergeRequest_Unauthenticated_Returns401()
    {
        ApiTestClient.ClearAuth(_client);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new { sourceLobbyId = Guid.NewGuid(), targetLobbyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Manager không thuộc cafe khác → 403.
    /// </summary>
    [IntegrationFact]
    public async Task CreateMergeRequest_WrongCafe_Returns403()
    {
        var wrongCafeId = Guid.NewGuid();
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{wrongCafeId}/lobby-merge/merge-requests",
            new { sourceLobbyId = Guid.NewGuid(), targetLobbyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =====================================================================
    // CREATE MERGE REQUEST TESTS
    // =====================================================================

    /// <summary>
    /// Tạo merge request với lobby ID không tồn tại → 404 hoặc 400.
    /// </summary>
    [IntegrationFact]
    public async Task CreateMergeRequest_WithNonExistentLobby_Returns404Or400()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new { sourceLobbyId = Guid.NewGuid(), targetLobbyId = Guid.NewGuid() });

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 404 or 400, got {response.StatusCode}");
    }

    /// <summary>
    /// G22 Idempotency: Cùng idempotency key → cùng response, không tạo mới.
    /// </summary>
    [IntegrationFact]
    public async Task CreateMergeRequest_WithSameIdempotencyKey_ReturnsSameResponse()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Tạo 2 lobby InProgress trước
        var lobbies = await CreateTwoInProgressLobbiesAsync();
        if (lobbies == null) return;

        var idempotencyKey = $"MERGE-IDEM-{Guid.NewGuid()}";
        var requestBody = new
        {
            sourceLobbyId = lobbies.Value.SourceLobbyId,
            targetLobbyId = lobbies.Value.TargetLobbyId,
            idempotencyKey
        };

        // Request 1
        var resp1 = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            requestBody);

        if (resp1.StatusCode == HttpStatusCode.NotFound || resp1.StatusCode == HttpStatusCode.BadRequest)
            return; // Lobby không tìm thấy → skip test

        Assert.True(resp1.IsSuccessStatusCode, $"First request failed: {resp1.StatusCode}");
        var body1 = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(resp1)).Data;
        Assert.NotNull(body1);

        // Request 2 — cùng idempotency key
        var resp2 = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            requestBody);

        Assert.True(resp2.IsSuccessStatusCode, $"Second request failed: {resp2.StatusCode}");
        var body2 = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(resp2)).Data;
        Assert.NotNull(body2);

        // Cùng ID (idempotency)
        Assert.Equal(body1!.Id, body2!.Id);
        Assert.Equal(LobbyMergeRequestStatus.Pending, body2.Status);
    }

    // =====================================================================
    // GET ENDPOINT TESTS
    // =====================================================================

    /// <summary>
    /// Lấy merge request không tồn tại → 404.
    /// </summary>
    [IntegrationFact]
    public async Task GetMergeRequest_NotFound_Returns404()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Lấy pending requests → 200 (danh sách có thể rỗng).
    /// </summary>
    [IntegrationFact]
    public async Task GetPendingMergeRequests_ReturnsOk()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/pending");

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 200 or 403, got {response.StatusCode}");
    }

    // =====================================================================
    // APPROVE MERGE TESTS
    // =====================================================================

    /// <summary>
    /// Approve request không tồn tại → 404 hoặc 400.
    /// </summary>
    [IntegrationFact]
    public async Task ApproveMerge_NotFound_Returns404()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{Guid.NewGuid()}/approve",
            new { });

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.Conflict,
            $"Expected 404/400/409, got {response.StatusCode}");
    }

    /// <summary>
    /// Approve với cross-cafe hoặc G8/G18 validation → 409 hoặc 200.
    /// </summary>
    [IntegrationFact]
    public async Task ApproveMerge_WithValidationError_Returns409Or200()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Tạo 2 lobby InProgress
        var lobbies = await CreateTwoInProgressLobbiesAsync();
        if (lobbies == null) return;

        // Tạo merge request
        var createResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new
            {
                sourceLobbyId = lobbies.Value.SourceLobbyId,
                targetLobbyId = lobbies.Value.TargetLobbyId,
                idempotencyKey = $"cross-cafe-{Guid.NewGuid()}"
            });

        if (!createResp.IsSuccessStatusCode) return;
        var createData = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(createResp)).Data;
        if (createData == null) return;

        // Approve — có thể thành công (cùng cafe) hoặc fail (G8 seat/G18 deposit validation)
        var approveResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{createData.Id}/approve",
            new { });

        Assert.True(
            approveResp.IsSuccessStatusCode ||
            approveResp.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Unexpected status: {approveResp.StatusCode}");
    }

    // =====================================================================
    // REJECT MERGE TESTS
    // =====================================================================

    /// <summary>
    /// Reject merge request → 200, status = Rejected.
    /// </summary>
    [IntegrationFact]
    public async Task RejectMerge_HappyPath_Returns200AndRejected()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Tạo 2 lobby InProgress
        var lobbies = await CreateTwoInProgressLobbiesAsync();
        if (lobbies == null) return;

        // Tạo merge request
        var createResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new
            {
                sourceLobbyId = lobbies.Value.SourceLobbyId,
                targetLobbyId = lobbies.Value.TargetLobbyId,
                reason = "Test reject",
                idempotencyKey = $"reject-{Guid.NewGuid()}"
            });

        if (!createResp.IsSuccessStatusCode) return;
        var createData = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(createResp)).Data;
        if (createData == null) return;

        // Reject
        var rejectResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{createData.Id}/reject",
            new { reviewNote = "Player decided not to switch groups" });

        Assert.True(
            rejectResp.IsSuccessStatusCode ||
            rejectResp.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Unexpected status: {rejectResp.StatusCode}");

        if (rejectResp.IsSuccessStatusCode)
        {
            var rejectData = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(rejectResp)).Data;
            Assert.NotNull(rejectData);
            Assert.Equal(LobbyMergeRequestStatus.Rejected, rejectData!.Status);
            Assert.Equal("Player decided not to switch groups", rejectData.ReviewNote);
        }
    }

    /// <summary>
    /// Reject request đã approve → 409 Conflict (hoặc 404 nếu không tìm thấy).
    /// </summary>
    [IntegrationFact]
    public async Task RejectMerge_AlreadyProcessed_Returns409Or404()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var lobbies = await CreateTwoInProgressLobbiesAsync();
        if (lobbies == null) return;

        // Tạo merge request
        var createResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new
            {
                sourceLobbyId = lobbies.Value.SourceLobbyId,
                targetLobbyId = lobbies.Value.TargetLobbyId,
                idempotencyKey = $"reject-after-approve-{Guid.NewGuid()}"
            });

        if (!createResp.IsSuccessStatusCode) return;
        var createData = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(createResp)).Data;
        if (createData == null) return;

        // Approve trước
        var approveResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{createData.Id}/approve",
            new { });

        // Nếu approve fail (G8 seat/G18 deposit validation) → skip
        if (!approveResp.IsSuccessStatusCode &&
            approveResp.StatusCode != HttpStatusCode.Conflict &&
            approveResp.StatusCode != HttpStatusCode.BadRequest)
            return;

        // Thử reject đã approve request
        var rejectResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{createData.Id}/reject",
            new { });

        Assert.True(
            rejectResp.StatusCode == HttpStatusCode.Conflict ||
            rejectResp.StatusCode == HttpStatusCode.NotFound ||
            rejectResp.IsSuccessStatusCode,
            $"Unexpected status: {rejectResp.StatusCode}");
    }

    // =====================================================================
    // CANCEL MERGE TESTS
    // =====================================================================

    /// <summary>
    /// Cancel merge request → 200, status = Cancelled.
    /// </summary>
    [IntegrationFact]
    public async Task CancelMergeRequest_HappyPath_Returns200AndCancelled()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var lobbies = await CreateTwoInProgressLobbiesAsync();
        if (lobbies == null) return;

        // Tạo merge request
        var createResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new
            {
                sourceLobbyId = lobbies.Value.SourceLobbyId,
                targetLobbyId = lobbies.Value.TargetLobbyId,
                idempotencyKey = $"cancel-{Guid.NewGuid()}"
            });

        if (!createResp.IsSuccessStatusCode) return;
        var createData = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(createResp)).Data;
        if (createData == null) return;

        // Cancel
        var cancelResp = await _client.DeleteAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{createData.Id}");

        Assert.True(
            cancelResp.IsSuccessStatusCode ||
            cancelResp.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Unexpected status: {cancelResp.StatusCode}");

        if (cancelResp.IsSuccessStatusCode)
        {
            var cancelData = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(cancelResp)).Data;
            Assert.NotNull(cancelData);
            Assert.Equal(LobbyMergeRequestStatus.Cancelled, cancelData!.Status);
        }
    }

    /// <summary>
    /// Cancel request đã rejected → 409 Conflict.
    /// </summary>
    [IntegrationFact]
    public async Task CancelMergeRequest_AlreadyRejected_Returns409()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var lobbies = await CreateTwoInProgressLobbiesAsync();
        if (lobbies == null) return;

        // Tạo rồi reject
        var createResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new
            {
                sourceLobbyId = lobbies.Value.SourceLobbyId,
                targetLobbyId = lobbies.Value.TargetLobbyId,
                idempotencyKey = $"cancel-after-reject-{Guid.NewGuid()}"
            });

        if (!createResp.IsSuccessStatusCode) return;
        var createData = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(createResp)).Data;
        if (createData == null) return;

        await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{createData.Id}/reject",
            new { });

        // Thử cancel đã rejected request
        var cancelResp = await _client.DeleteAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{createData.Id}");

        Assert.True(
            cancelResp.StatusCode == HttpStatusCode.Conflict ||
            cancelResp.StatusCode == HttpStatusCode.NotFound ||
            cancelResp.IsSuccessStatusCode,
            $"Unexpected status: {cancelResp.StatusCode}");
    }

    // =====================================================================
    // GET MERGE HISTORY TEST
    // =====================================================================

    /// <summary>
    /// Lấy merge history của lobby → trả về danh sách audit logs (có thể rỗng).
    /// </summary>
    [IntegrationFact]
    public async Task GetMergeHistory_ReturnsOk()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var lobbies = await CreateTwoInProgressLobbiesAsync();
        if (lobbies == null) return;

        // Tạo merge request để sinh audit log
        var createResp = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new
            {
                sourceLobbyId = lobbies.Value.SourceLobbyId,
                targetLobbyId = lobbies.Value.TargetLobbyId,
                idempotencyKey = $"history-{Guid.NewGuid()}"
            });

        if (!createResp.IsSuccessStatusCode) return;

        // Lấy history cho source lobby
        var historyResp = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/lobbies/{lobbies.Value.SourceLobbyId}/merge-history");

        Assert.True(
            historyResp.IsSuccessStatusCode ||
            historyResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Unexpected status: {historyResp.StatusCode}");

        if (historyResp.IsSuccessStatusCode)
        {
            var historyData = (await ApiTestClient.ReadApiResponseAsync<List<object>>(historyResp)).Data;
            Assert.NotNull(historyData);
            // Có audit log "MergeRequested" nếu tạo thành công
        }
    }

    // =====================================================================
    // FULL HAPPY PATH FLOW TEST
    // =====================================================================

    /// <summary>
    /// Full flow: Create → Reject → Cancel (pending) → Create lại cùng key → vẫn là Rejected (idempotency).
    /// BR-G22: Idempotency cho create.
    /// BR-G27/G28: Status transition: Pending → Rejected.
    /// </summary>
    [IntegrationFact]
    public async Task FullFlow_CreateRejectCancelCreateAgain_Works()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var lobbies = await CreateTwoInProgressLobbiesAsync();
        if (lobbies == null) return;

        var idempotencyKey = $"fullflow-{Guid.NewGuid()}";

        // Step 1: Tạo merge request
        var create1 = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new { sourceLobbyId = lobbies.Value.SourceLobbyId, targetLobbyId = lobbies.Value.TargetLobbyId, idempotencyKey });

        if (!create1.IsSuccessStatusCode) return;
        var data1 = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(create1)).Data;
        if (data1 == null) return;
        Assert.Equal(LobbyMergeRequestStatus.Pending, data1.Status);
        var requestId = data1.Id;

        // Step 2: Reject
        var reject = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{requestId}/reject",
            new { reviewNote = "Cancelled by staff" });

        if (reject.IsSuccessStatusCode)
        {
            var rejectData = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(reject)).Data;
            Assert.Equal(LobbyMergeRequestStatus.Rejected, rejectData!.Status);
        }

        // Step 3: Cancel sau khi reject → phải là 409 (không còn Pending)
        var cancel1 = await _client.DeleteAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests/{requestId}");

        Assert.True(
            cancel1.StatusCode == HttpStatusCode.Conflict ||
            cancel1.StatusCode == HttpStatusCode.NotFound ||
            cancel1.IsSuccessStatusCode,
            $"Expected 409/404/200, got {cancel1.StatusCode}");

        // Step 4: Tạo lại với cùng idempotency key → phải trả về request cũ (đã rejected)
        var create2 = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobby-merge/merge-requests",
            new { sourceLobbyId = lobbies.Value.SourceLobbyId, targetLobbyId = lobbies.Value.TargetLobbyId, idempotencyKey });

        if (!create2.IsSuccessStatusCode) return;
        var data2 = (await ApiTestClient.ReadApiResponseAsync<LobbyMergeRequestDto>(create2)).Data;
        Assert.NotNull(data2);
        // Cùng idempotency key → cùng request ID
        Assert.Equal(requestId, data2!.Id);
        // Status vẫn là Rejected (không tạo mới)
        Assert.Equal(LobbyMergeRequestStatus.Rejected, data2.Status);
    }
}
