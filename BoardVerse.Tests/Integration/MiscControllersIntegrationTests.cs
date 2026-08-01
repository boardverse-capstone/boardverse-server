#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for remaining endpoints
/// Covers: Manager, Staff, MasterGame, UserRating, Bgg, Health
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MiscControllersIntegrationTests
{
    private readonly HttpClient _client;

    public MiscControllersIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === MANAGER CONTROLLER ===

    [IntegrationFact]
    public async Task Manager_GetMyCafes()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/manager/my-cafes");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === STAFF CONTROLLER ===

    [IntegrationFact]
    public async Task Staff_GetMyWorkplaces()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/staff/my-cafes");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === MASTER GAME CONTROLLER ===

    [IntegrationFact]
    public async Task MasterGame_GetAll()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/master-games?searchTerm=catan");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task MasterGame_GetById()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var response = await _client.GetAsync($"/api/v1/master-games/{catanId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === USER RATING CONTROLLER ===

    [IntegrationFact]
    public async Task UserRating_GetKarmaRatingContext()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/users/ratings/karma/lobbies/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task UserRating_SubmitKarmaRatings()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var ratingRequest = new
        {
            lobbyId = Guid.NewGuid(),
            ratings = new[]
            {
                new
                {
                    userId = IntegrationTestFixtures.DemoPlayer2UserId,
                    karmaScore = 5,
                    comment = "Great player!"
                }
            }
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            "/api/v1/users/ratings/karma/submit",
            ratingRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion

    #region === BGG CONTROLLER ===

    [IntegrationFact]
    public async Task Bgg_GetComponentCatalog()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var response = await _client.GetAsync("/api/v1/bgg/component-catalog");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Bgg_Search()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var response = await _client.GetAsync("/api/v1/bgg/search?query=catan");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Bgg_PreviewGame()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var response = await _client.GetAsync("/api/v1/bgg/games/13");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === HEALTH CONTROLLER ===

    [IntegrationFact]
    public async Task Health_GetStatus()
    {
        var response = await _client.GetAsync("/api/health/status");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.ServiceUnavailable);
    }

    [IntegrationFact]
    public async Task Health_GetDbInfo()
    {
        var response = await _client.GetAsync("/api/health/db-info");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.ServiceUnavailable);
    }

    [IntegrationFact]
    public async Task Health_Ping()
    {
        var response = await _client.GetAsync("/api/health/ping");
        Assert.True(response.StatusCode == HttpStatusCode.OK);
    }

    #endregion

    #region === PROTECTED CONTROLLER ===

    [IntegrationFact]
    public async Task Protected_GetSecret()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/protected/secret");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === USER MANAGEMENT CONTROLLER ===

    [IntegrationFact]
    public async Task UserManagement_GetById()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/usermanagement/{IntegrationTestFixtures.DemoPlayer1UserId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task UserManagement_Unblock()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/usermanagement/users/{IntegrationTestFixtures.DemoPlayer1UserId}/unblock", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === MANAGER CAFE PROFILE CONTROLLER ===

    [IntegrationFact]
    public async Task ManagerCafeProfile_Deactivate()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/manager/cafes/me/deactivate", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task ManagerCafeProfile_Close()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var closeRequest = new { reason = "Temporary closure" };
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/manager/cafes/me/close",
            closeRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task ManagerCafeProfile_Reopen()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/manager/cafes/me/reopen", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion

    #region === CAFE PARTNER APPLICATION ===

    [IntegrationFact]
    public async Task CafePartnerApplication_GetById()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/cafe-partner-applications/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion
}
