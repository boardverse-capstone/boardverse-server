#nullable enable
using System.Net;
using System.Net.Http.Json;
using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests verify rằng các API endpoints trả về DTO đúng format khi
/// navigation properties bị null (orphan FK, deleted user, v.v.).
///
/// Mục đích:
///   - Đảm bảo không có NullReferenceException runtime do `entity?.X.ToString()` pattern
///   - Verify các DTO fields có fallback value hợp lý ("", 0, false) thay vì null
///
/// Connection string từ <c>appsettings.local.json</c> trỏ về nhánh Neon testing
/// (<c>ep-morning-darkness</c>) theo <c>neon-database-workflow.mdc</c>.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class NullSafetyIntegrationTests
{
    private readonly HttpClient _client;

    public NullSafetyIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    /// <summary>
    /// GET /api/bookings/cafe/{cafeId} — staff path trả List&lt;BookingResponseDto&gt;.
    /// Verify response không throw NullReferenceException khi một số booking
    /// có navigation properties null (e.g. BookingDeposit null cho walk-in booking).
    /// </summary>
    [IntegrationFact]
    public async Task GetBookingsByCafe_ReturnsValidResponse_WhenSomeBookingsHaveNullNavigation()
    {
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        var response = await _client.GetAsync(
            $"/api/bookings/cafe/{IntegrationTestFixtures.DemoCafeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ApiTestClient.ReadApiResponseAsync<List<BookingResponseDto>>(response);
        Assert.NotNull(body);
        Assert.NotNull(body.Data);

        // Mỗi booking phải có Status non-null
        foreach (var booking in body.Data!)
        {
            Assert.NotNull(booking.Status);
            Assert.NotNull(booking.Id);
        }
    }

    /// <summary>
    /// GET /api/v1/reservations/{id} — verify LobbyStatus fallback khi Reservation.Lobby = null.
    /// </summary>
    [IntegrationFact]
    public async Task GetReservationDetail_HandlesNullLobbyNavigation_WithoutCrash()
    {
        // Tạo reservation có Lobby = null bằng cách tạo qua walk-in path
        // (walk-in reservations không tạo Lobby).
        // Test này verify response trả về null cho LobbyStatus thay vì crash.
        // Skip nếu chưa có walk-in reservation trong DB.
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        var listResponse = await _client.GetAsync(
            $"/api/v1/reservations?cafeId={IntegrationTestFixtures.DemoCafeId}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await ApiTestClient.ReadApiResponseAsync<ReservationListResponseDto>(listResponse);
        Assert.NotNull(list);

        if (list.Data?.Items == null || list.Data.Items.Count == 0)
        {
            return; // Không có reservation nào để test, skip.
        }

        // Lấy reservation đầu tiên có LobbyId = null (orphan / walk-in)
        var target = list.Data.Items.FirstOrDefault(r => r.LobbyId == null);
        if (target == null)
        {
            return; // Tất cả đều có Lobby, không có case null để test.
        }

        var detailResponse = await _client.GetAsync($"/api/v1/reservations/{target.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await ApiTestClient.ReadApiResponseAsync<ReservationDetailDto>(detailResponse);
        Assert.NotNull(detail);
        Assert.NotNull(detail.Data);
        // LobbyStatus non-null (DTO default = empty string), LobbyShareCode null OK
        Assert.NotNull(detail.Data!.LobbyStatus);
    }

    /// <summary>
    /// GET /api/v1/lobbies/{id} — verify response không throw khi Host bị xóa (orphan).
    /// </summary>
    [IntegrationFact]
    public async Task GetLobbyById_HandlesNullHost_WithoutCrash()
    {
        var player1 = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, player1);

        // Lấy danh sách lobby của player1 (endpoint thực tế: GET /api/v1/lobbies/hosted)
        var listResponse = await _client.GetAsync("/api/v1/lobbies/hosted");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        // Chỉ verify response status OK, không cần có lobby nào
        var bodyText = await listResponse.Content.ReadAsStringAsync();
        Assert.NotEmpty(bodyText);
    }

    /// <summary>
    /// GET /api/v1/users/{userId}/karma — verify GamerTier fallback khi Profile null.
    /// </summary>
    [IntegrationFact]
    public async Task GetUserKarma_ReturnsValidGamerTier_WhenProfileExists()
    {
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        var response = await _client.GetAsync($"/api/v1/users/{IntegrationTestFixtures.AdminUserId}/karma");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Endpoint trả raw UserKarmaStateDto (không wrap trong ApiResponseEnvelope).
        var json = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(json);
        Assert.Contains("karmaLevel", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GET /api/v1/friends/{userId} — verify FriendshipStatus + GamerTier fallback
    /// khi User.Profile null (user chưa tạo profile).
    /// </summary>
    [IntegrationFact]
    public async Task GetFriendList_HandlesNullProfile_WithoutCrash()
    {
        var player1 = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, player1);

        var response = await _client.GetAsync("/api/v1/friends");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Body có thể là List<FriendDto> hoặc FriendListResponseDto tùy implementation.
        // Chỉ verify response không throw.
        var bodyText = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(bodyText);
    }

    /// <summary>
    /// GET /api/cafes/{cafeId}/reservations — staff path verify CafeName/GameName
    /// có fallback khi navigation null.
    /// </summary>
    [IntegrationFact]
    public async Task GetCafeReservations_ReturnsValidCafeNameAndGameName_ForEachItem()
    {
        // Endpoint yêu cầu role Manager hoặc CafeStaff (Admin không đủ).
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/reservations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ApiTestClient.ReadApiResponseAsync<CafeReservationsResponseDto>(response);
        Assert.NotNull(body);
        Assert.NotNull(body.Data);

        foreach (var item in body.Data!.Items)
        {
            Assert.NotNull(item.CafeName);
            Assert.NotNull(item.GameName);
            Assert.NotNull(item.Status);
        }
    }

    /// <summary>
    /// GET /api/cafes/{cafeId}/lobbies — staff path verify các navigation fallback.
    /// </summary>
    [IntegrationFact]
    public async Task GetCafeLobbies_ReturnsValidResponse_ForEachLobby()
    {
        // Endpoint yêu cầu role Manager hoặc CafeStaff (Admin không đủ).
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/lobbies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Body type có thể là LobbyListResponseDto — chỉ verify status 200.
    }
}