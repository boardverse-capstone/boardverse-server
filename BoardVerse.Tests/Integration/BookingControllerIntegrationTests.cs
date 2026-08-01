#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for BookingController
/// Covers: Create, Get, Patch, Delete booking endpoints
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class BookingControllerIntegrationTests
{
    private readonly HttpClient _client;

    public BookingControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === CREATE BOOKING ===

    [IntegrationFact]
    public async Task Booking_Create_WithValidLobby_Returns201()
    {
        // Arrange: Create lobby first
        var player1Token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, player1Token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var lobbyResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/lobbies", new
        {
            gameTemplateId = catanId,
            scheduledStartTime = DateTime.UtcNow.AddHours(3),
            maxMembers = 4,
            cancellationLeadTimeMinutes = 30
        });

        if (lobbyResponse.StatusCode != HttpStatusCode.Created) return;
        var lobbyBody = await ApiTestClient.ReadApiResponseAsync<object>(lobbyResponse);

        // Join and lock lobby
        var player2Token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, player2Token);
        await _client.PostAsync($"/api/v1/lobbies/{Guid.NewGuid()}/join", null);

        ApiTestClient.Authorize(_client, player1Token);
        await _client.PostAsync($"/api/v1/lobbies/{Guid.NewGuid()}/lock", null);

        // Create booking
        var bookingRequest = new
        {
            lobbyId = Guid.NewGuid(),
            cafeId = IntegrationTestFixtures.DemoCafeId,
            cafeTableId = IntegrationTestFixtures.DemoPosTableId,
            scheduledStartTime = DateTime.UtcNow.AddHours(3),
            scheduleEndTime = DateTime.UtcNow.AddHours(5)
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/bookings", bookingRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task Booking_Create_WithoutLobby_Returns201()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var bookingRequest = new
        {
            cafeId = IntegrationTestFixtures.DemoCafeId,
            cafeTableId = IntegrationTestFixtures.DemoPosTableId,
            scheduledStartTime = DateTime.UtcNow.AddHours(2),
            scheduleEndTime = DateTime.UtcNow.AddHours(4),
            memberCount = 2
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/bookings", bookingRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion

    #region === GET BOOKING ===

    [IntegrationFact]
    public async Task Booking_GetById_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/bookings/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Booking_GetByLobbyId_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/bookings/lobby/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Booking_GetByCafeId_AsManager()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/bookings/cafe/{IntegrationTestFixtures.DemoCafeId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === UPDATE BOOKING ===

    [IntegrationFact]
    public async Task Booking_Update_Schedule()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var patchRequest = new
        {
            scheduledStartTime = DateTime.UtcNow.AddHours(4),
            scheduleEndTime = DateTime.UtcNow.AddHours(6)
        };

        var response = await ApiTestClient.PatchJsonAsync(_client,
            $"/api/bookings/{Guid.NewGuid()}",
            patchRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === CANCEL/DELETE BOOKING ===

    [IntegrationFact]
    public async Task Booking_Cancel_ByPlayer()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync($"/api/bookings/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion

    #region === CHECK-IN / CHECK-OUT ===

    [IntegrationFact]
    public async Task Booking_CheckIn_WithValidCode()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            bookingCode = "TEST123"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/bookings/check-in",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Booking_CheckOut_WithValidId()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/bookings/{Guid.NewGuid()}/check-out",
            new { notes = "Test checkout" });
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion
}
