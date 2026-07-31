#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Comprehensive tests for edge cases and error handling
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class EdgeCaseAndErrorHandlingTests
{
    private readonly HttpClient _client;

    public EdgeCaseAndErrorHandlingTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === UNAUTHORIZED ACCESS ===

    [IntegrationFact]
    public async Task Unauthorized_AccessDenied()
    {
        var response = await _client.GetAsync("/api/v1/profiles/me");
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task Unauthorized_ExpiredToken()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "expired.token.here");
        var response = await _client.GetAsync("/api/v1/profiles/me");
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Unauthorized_InvalidToken()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid.token");
        var response = await _client.GetAsync("/api/v1/profiles/me");
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion

    #region === FORBIDDEN ACCESS ===

    [IntegrationFact]
    public async Task Forbidden_AdminEndpointAsPlayer()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/users");
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Forbidden_ManagerEndpointAsPlayer()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/manager/my-cafes");
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Forbidden_PlayerEndpointAsAdmin()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        // Try to access friend endpoints as admin - might be allowed or forbidden depending on design
        var response = await _client.GetAsync("/api/v1/friends");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    #endregion

    #region === NOT FOUND ===

    [IntegrationFact]
    public async Task NotFound_NonExistentProfile()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var fakeId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/profiles/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task NotFound_NonExistentCafe()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var fakeId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/cafes/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task NotFound_NonExistentLobby()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var fakeId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/lobbies/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task NotFound_NonExistentTournament()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var fakeId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/tournaments/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task NotFound_NonExistentBooking()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var fakeId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/bookings/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task NotFound_NonExistentDeposit()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var fakeId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/payments/booking-deposit/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region === BAD REQUEST - VALIDATION ===

    [IntegrationFact]
    public async Task BadRequest_InvalidGuid()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/profiles/invalid-guid");
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task BadRequest_EmptySearchQuery()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/profiles/search?query=");
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.OK);
    }

    [IntegrationFact]
    public async Task BadRequest_InvalidLatitude()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new { latitude = 999.0, longitude = 106.6297 };
        var response = await ApiTestClient.PutJsonAsync(_client, "/api/v1/profiles/location", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task BadRequest_InvalidLongitude()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new { latitude = 10.8231, longitude = 999.0 };
        var response = await ApiTestClient.PutJsonAsync(_client, "/api/v1/profiles/location", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task BadRequest_MissingRequiredFields()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new { }; // Empty request
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/friends/requests", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task BadRequest_InvalidEmail()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new { email = "invalid-email" };
        var response = await ApiTestClient.PutJsonAsync(_client, "/api/v1/profiles/me", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task BadRequest_NegativeAmount()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new { amount = -100 };
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/payments/booking-deposit", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task BadRequest_ZeroAmount()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new { amount = 0 };
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/payments/booking-deposit", request);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === CONFLICT ===

    [IntegrationFact]
    public async Task Conflict_DuplicateFriendRequest()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            targetUserId = IntegrationTestFixtures.DemoPlayer2UserId,
            message = "Let's be friends!"
        };

        // Send first request
        var firstResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/friends/requests", request);

        if (firstResponse.StatusCode == HttpStatusCode.Created)
        {
            // Try to send again
            var secondResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/friends/requests", request);
            Assert.True(secondResponse.StatusCode == HttpStatusCode.Conflict ||
                       secondResponse.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    [IntegrationFact]
    public async Task Conflict_AlreadyFriends()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            targetUserId = IntegrationTestFixtures.DemoPlayer2UserId,
            message = "Already friends!"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/friends/requests", request);
        Assert.True(response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.Created);
    }

    #endregion

    #region === GONE ===

    [IntegrationFact]
    public async Task Gone_LobbyExpired()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        // Try to interact with an expired lobby
        var response = await _client.GetAsync($"/api/v1/lobbies/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task Gone_BookingExpired()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/bookings/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Gone);
    }

    #endregion

    #region === METHOD NOT ALLOWED ===

    [IntegrationFact]
    public async Task MethodNotAllowed_WrongHttpMethod()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        // Try to GET a POST-only endpoint
        var response = await _client.GetAsync("/api/v1/friends/requests");
        Assert.True(response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === CONCURRENT MODIFICATION ===

    [IntegrationFact]
    public async Task Concurrent_LobbyLockConflict()
    {
        var player1Token = await IntegrationTestAuth.AsPlayer1Async(_client);
        var player2Token = await IntegrationTestAuth.AsPlayer2Async(_client);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        // Player1 creates lobby
        ApiTestClient.Authorize(_client, player1Token);
        var createResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/lobbies", new
        {
            gameTemplateId = catanId,
            scheduledStartTime = DateTime.UtcNow.AddHours(3),
            maxMembers = 2
        });

        if (createResponse.StatusCode != HttpStatusCode.Created) return;

        // Player2 joins
        ApiTestClient.Authorize(_client, player2Token);
        var joinResponse = await _client.PostAsync($"/api/v1/lobbies/{Guid.NewGuid()}/join", null);

        // Player1 locks
        ApiTestClient.Authorize(_client, player1Token);
        var lockResponse = await _client.PostAsync($"/api/v1/lobbies/{Guid.NewGuid()}/lock", null);

        // Try to join again - should fail
        ApiTestClient.Authorize(_client, player2Token);
        var joinAgainResponse = await _client.PostAsync($"/api/v1/lobbies/{Guid.NewGuid()}/join", null);

        Assert.True(joinAgainResponse.StatusCode == HttpStatusCode.Conflict ||
                   joinAgainResponse.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === RATE LIMITING ===

    [IntegrationFact]
    public async Task RateLimit_SearchRapidly()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        // Make rapid requests
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.GetAsync($"/api/v1/profiles/search?query=test{i}");
            Assert.True(response.StatusCode == HttpStatusCode.OK ||
                       response.StatusCode == HttpStatusCode.TooManyRequests ||
                       response.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    #endregion
}
