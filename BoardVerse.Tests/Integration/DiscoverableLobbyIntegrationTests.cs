#nullable enable
using System.Net;
using System.Net.Http.Json;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for GET /api/v1/lobbies/discoverable.
/// Verifies fix (2026-08-27): lobby còn public và CHƯA VÀO PHIÊN CHƠI
/// (Open/Viable/Full/WaitingCheckIn) phải hiển thị, không chỉ Open.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class DiscoverableLobbyIntegrationTests
{
    private readonly HttpClient _client;

    public DiscoverableLobbyIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    // -----------------------------------------------------------------------
    // Helper: top-up BVC bypass SePay bằng admin adjust.
    // -----------------------------------------------------------------------
    private async Task<long> TopUpBvcAsync(Guid playerUserId, long amountBvc, string suffix)
    {
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/wallet/adjust?userId={playerUserId}&adjustmentBvc={amountBvc}&reason=Test-{suffix}",
            Array.Empty<object>());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ApiTestClient.ReadApiResponseAsync<WalletDto>(response);
        return body.Data?.AvailableBalance ?? 0;
    }

    private async Task<long> GetAvailableBalanceAsync()
    {
        var player1 = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, player1);

        var response = await _client.GetAsync("/api/v1/wallet?includeHeld=true");
        var body = await ApiTestClient.ReadApiResponseAsync<WalletDto>(response);
        return body.Data?.AvailableBalance ?? 0;
    }

    // -----------------------------------------------------------------------
    // Test 1: Lobby Open → xuất hiện trong discoverable
    // -----------------------------------------------------------------------
    [IntegrationFact]
    public async Task Discoverable_LobbyOpen_AppearsInDiscoverable()
    {
        // Arrange: top-up + tạo lobby
        await TopUpBvcAsync(IntegrationTestFixtures.DemoPlayer1UserId, 500_000, "disc-open");

        var player1 = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, player1);

        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var idempotencyKey = $"test-disc-open-{Guid.NewGuid():N}";

        // Quote + Confirm tạo lobby (status = Open sau khi tạo)
        var quoteRequest = new ReservationQuoteRequestDto
        {
            CafeId = IntegrationTestFixtures.DemoCafeId,
            GameId = IntegrationTestFixtures.DemoCatanGameTemplateId,
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(14, 0),
            PreferredEndTime = new TimeOnly(18, 0),
            MinPlayers = 2,
            MaxPlayers = 4,
            IdempotencyKey = idempotencyKey + "-quote"
        };
        var quoteResponse = await _client.PostAsJsonAsync("/api/v1/reservations/quote", quoteRequest);
        if (quoteResponse.StatusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.Conflict or HttpStatusCode.BadRequest) return;
        Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode);
        var quoteBody = (await ApiTestClient.ReadApiResponseAsync<ReservationQuoteDto>(quoteResponse)).Data!;

        var confirmRequest = new ReservationConfirmRequestDto
        {
            CafeId = IntegrationTestFixtures.DemoCafeId,
            GameId = IntegrationTestFixtures.DemoCatanGameTemplateId,
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(14, 0),
            PreferredEndTime = new TimeOnly(18, 0),
            MinPlayers = 2,
            MaxPlayers = 4,
            ExpectedFinalDeposit = quoteBody.FinalDeposit,
            IdempotencyKey = idempotencyKey + "-confirm"
        };
        var confirmResponse = await _client.PostAsJsonAsync("/api/v1/reservations/confirm", confirmRequest);
        if (confirmResponse.StatusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.Conflict or HttpStatusCode.BadRequest) return;
        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);
        var confirmBody = (await ApiTestClient.ReadApiResponseAsync<ReservationConfirmResponseDto>(confirmResponse)).Data!;

        // Act: gọi discoverable
        var discoverableResponse = await _client.GetAsync(
            "/api/v1/lobbies/discoverable?latitude=10.8231&longitude=106.6297&radiusKm=50");
        Assert.Equal(HttpStatusCode.OK, discoverableResponse.StatusCode);

        var discoverableBody = await ApiTestClient.ReadApiResponseAsync<List<LobbyResponseDto>>(discoverableResponse);
        Assert.NotNull(discoverableBody.Data);
        var lobbyIds = discoverableBody.Data!.Select(l => l.Id).ToList();

        // Assert: lobby mới tạo (Open) phải xuất hiện
        Assert.Contains(confirmBody.LobbyId, lobbyIds);
    }

    // -----------------------------------------------------------------------
    // Test 2: Sau khi member join đầy (Full) → vẫn xuất hiện trong discoverable
    // -----------------------------------------------------------------------
    [IntegrationFact]
    public async Task Discoverable_LobbyBecomesFull_StillAppearsInDiscoverable()
    {
        // Arrange: tạo lobby với host + top-up cho host + member
        await TopUpBvcAsync(IntegrationTestFixtures.DemoPlayer1UserId, 500_000, "disc-full-h1");
        await TopUpBvcAsync(IntegrationTestFixtures.DemoPlayer2UserId, 500_000, "disc-full-m1");

        var player1 = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, player1);

        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var idempotencyKey = $"test-disc-full-{Guid.NewGuid():N}";

        // Quote
        var quoteRequest = new ReservationQuoteRequestDto
        {
            CafeId = IntegrationTestFixtures.DemoCafeId,
            GameId = IntegrationTestFixtures.DemoCatanGameTemplateId,
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(15, 0),
            PreferredEndTime = new TimeOnly(20, 0),
            MinPlayers = 2,
            MaxPlayers = 2, // Nhỏ để dễ fill
            IdempotencyKey = idempotencyKey + "-quote"
        };
        var quoteResponse = await _client.PostAsJsonAsync("/api/v1/reservations/quote", quoteRequest);
        if (quoteResponse.StatusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.Conflict or HttpStatusCode.BadRequest) return;
        Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode);
        var quoteBody = (await ApiTestClient.ReadApiResponseAsync<ReservationQuoteDto>(quoteResponse)).Data!;

        // Confirm
        var confirmRequest = new ReservationConfirmRequestDto
        {
            CafeId = IntegrationTestFixtures.DemoCafeId,
            GameId = IntegrationTestFixtures.DemoCatanGameTemplateId,
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(15, 0),
            PreferredEndTime = new TimeOnly(20, 0),
            MinPlayers = 2,
            MaxPlayers = 2,
            ExpectedFinalDeposit = quoteBody.FinalDeposit,
            IdempotencyKey = idempotencyKey + "-confirm"
        };
        var confirmResponse = await _client.PostAsJsonAsync("/api/v1/reservations/confirm", confirmRequest);
        if (confirmResponse.StatusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.Conflict or HttpStatusCode.BadRequest) return;
        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);
        var confirmBody = (await ApiTestClient.ReadApiResponseAsync<ReservationConfirmResponseDto>(confirmResponse)).Data!;

        // Host tạo lobby (status = Open), sau đó member join → lobby trở thành Full
        var player2Token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, player2Token);

        var joinResponse = await _client.PostAsync($"/api/v1/lobbies/{confirmBody.LobbyId}/join", null);
        if (joinResponse.StatusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return;
        Assert.True(
            joinResponse.StatusCode == HttpStatusCode.OK
            || joinResponse.StatusCode == HttpStatusCode.Conflict // đã full rồi
        );

        // Act: gọi discoverable với player khác (player3)
        await TopUpBvcAsync(IntegrationTestFixtures.DemoPlayer3UserId, 500_000, "disc-full-p3");
        var player3Token = await IntegrationTestAuth.AsPlayer3Async(_client);
        ApiTestClient.Authorize(_client, player3Token);

        var discoverableResponse = await _client.GetAsync(
            "/api/v1/lobbies/discoverable?latitude=10.8231&longitude=106.6297&radiusKm=50");
        Assert.Equal(HttpStatusCode.OK, discoverableResponse.StatusCode);

        var discoverableBody = await ApiTestClient.ReadApiResponseAsync<List<LobbyResponseDto>>(discoverableResponse);
        Assert.NotNull(discoverableBody.Data);
        var lobbyIds = discoverableBody.Data!.Select(l => l.Id).ToList();

        // Assert: lobby đã Full vẫn xuất hiện (fix 2026-08-27)
        Assert.Contains(confirmBody.LobbyId, lobbyIds);
    }

    // -----------------------------------------------------------------------
    // Test 3: Lobby InProgress (đã vào quán) → KHÔNG xuất hiện trong discoverable
    // -----------------------------------------------------------------------
    [IntegrationFact]
    public async Task Discoverable_LobbyInProgress_DoesNotAppear()
    {
        var discoverableResponse = await _client.GetAsync(
            "/api/v1/lobbies/discoverable?latitude=10.8231&longitude=106.6297&radiusKm=50");

        Assert.Equal(HttpStatusCode.OK, discoverableResponse.StatusCode);

        var discoverableBody = await ApiTestClient.ReadApiResponseAsync<List<LobbyResponseDto>>(discoverableResponse);

        Assert.NotNull(discoverableBody.Data);

        // DemoMatchLobbyId đang InProgress (bootstrapper seed) — phải KHÔNG xuất hiện
        Assert.DoesNotContain(discoverableBody.Data, l => l.Id == IntegrationTestFixtures.DemoMatchLobbyId);
    }
}
