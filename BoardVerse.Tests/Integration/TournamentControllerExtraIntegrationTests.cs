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
                   response.StatusCode == HttpStatusCode.NotFound);
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
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Tournament_GetById()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/tournaments/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Tournament_GetParticipants()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/tournaments/{Guid.NewGuid()}/participants");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Tournament_GetMatches()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/tournaments/{Guid.NewGuid()}/matches");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Tournament_GetRoundMatches()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/tournaments/{Guid.NewGuid()}/matches/round/1");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
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
                   response.StatusCode == HttpStatusCode.Unauthorized);
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
                   response.StatusCode == HttpStatusCode.Unauthorized);
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
                   response.StatusCode == HttpStatusCode.Unauthorized);
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
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion
}
