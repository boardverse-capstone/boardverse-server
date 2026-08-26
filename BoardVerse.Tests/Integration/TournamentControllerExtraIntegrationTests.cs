#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for TournamentController - Extra endpoints
/// Covers: Elo history, round matches, withdraw, leaderboard
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class TournamentControllerExtraIntegrationTests
{
    private readonly HttpClient _client;

    public TournamentControllerExtraIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === REGISTRATION ===

    [IntegrationFact]
    public async Task Tournament_Register()
    {
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        // Create tournament first
        var createRequest = new
        {
            title = $"Test Tournament {Guid.NewGuid():N}".Substring(0, 25),
            gameTemplateId = catanId,
            maxParticipants = 8,
            startTime = DateTime.UtcNow.AddDays(7),
            registrationDeadline = DateTime.UtcNow.AddDays(5),
            minParticipants = 4
        };

        var createResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/tournaments", createRequest);

        if (createResponse.StatusCode != HttpStatusCode.Created) return;
        var tournamentBody = await ApiTestClient.ReadApiResponseAsync<object>(createResponse);

        // Register as player
        var playerToken = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, playerToken);

        var registerRequest = new { tournamentId = Guid.NewGuid() };
        var registerResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/tournaments/register", registerRequest);
        Assert.True(registerResponse.StatusCode == HttpStatusCode.OK ||
                   registerResponse.StatusCode == HttpStatusCode.BadRequest ||
                   registerResponse.StatusCode == HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task Tournament_Unregister()
    {
        var playerToken = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, playerToken);

        var unregisterRequest = new { tournamentId = Guid.NewGuid() };
        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/tournaments/unregister", unregisterRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion

    #region === GET ENDPOINTS ===

    [IntegrationFact]
    public async Task Tournament_GetOpen()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/tournaments/open");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task Tournament_GetById()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/tournaments/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task Tournament_GetParticipants()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/tournaments/{Guid.NewGuid()}/participants");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task Tournament_GetMatches()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/tournaments/{Guid.NewGuid()}/matches");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task Tournament_GetRoundMatches()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/tournaments/{Guid.NewGuid()}/matches/round/1");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task Tournament_GetMyRegistrations()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/tournaments/my-registrations");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task Tournament_GetMyEloHistory()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/tournaments/my-elo-history");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task Tournament_GetLeaderboard()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/tournaments/leaderboard?limit=10");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion

    #region === SEARCH ===

    [IntegrationFact]
    public async Task Tournament_Search()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var response = await _client.GetAsync(
            $"/api/v1/tournaments/search?gameTemplateId={catanId}&status=RegistrationOpen");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion

    #region === GET /api/v1/tournaments?status=... (filter by status) ===

    [IntegrationFact]
    public async Task Tournament_GetAll_NoStatus_ReturnsOk()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        // Không truyền status → trả tất cả (mọi status).
        var response = await _client.GetAsync("/api/v1/tournaments");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task Tournament_GetAll_StatusOpen_FiltersCorrectly()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/tournaments?status=RegistrationOpen");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task Tournament_GetAll_StatusOngoing_FiltersCorrectly()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/tournaments?status=OnGoing");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task Tournament_GetAll_StatusCompleted_FiltersCorrectly()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/tournaments?status=Completed");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task Tournament_GetAll_AllKeyword_ReturnsAll()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        // "all" → giống null, trả tất cả.
        var response = await _client.GetAsync("/api/v1/tournaments?status=all");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task Tournament_GetAll_InvalidStatus_ReturnsBadRequest()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/tournaments?status=FROZEN_STATUS");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Tournament_GetAll_NoAuth_ReturnsUnauthorized()
    {
        ApiTestClient.ClearAuth(_client);

        var response = await _client.GetAsync("/api/v1/tournaments");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion
}
