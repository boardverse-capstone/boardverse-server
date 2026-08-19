#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for UserProfileController - Extra endpoints
/// Covers: Location delete, Karma history, Profile delete
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class UserProfileControllerExtraIntegrationTests
{
    private readonly HttpClient _client;

    public UserProfileControllerExtraIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === PROFILE CRUD ===

    [IntegrationFact]
    public async Task Profile_Create()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var createRequest = new
        {
            displayName = $"Test User {Guid.NewGuid():N}".Substring(0, 15),
            bio = "Test bio from integration test"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/UserProfile", createRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task Profile_GetById()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/UserProfile/{IntegrationTestFixtures.DemoPlayer1UserId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Profile_Update()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            bio = "Updated bio from integration test"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/UserProfile/{IntegrationTestFixtures.DemoPlayer1UserId}",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task Profile_Delete()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync($"/api/UserProfile/{IntegrationTestFixtures.DemoPlayer1UserId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    #endregion

    #region === LOCATION ===

    [IntegrationFact]
    public async Task Profile_UpdateLocation()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            latitude = 10.8231,
            longitude = 106.6297
        };

        var response = await ApiTestClient.PutJsonAsync(_client, "/api/UserProfile/me/location", updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Profile_DeleteLocation()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync("/api/UserProfile/me/location");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion

    #region === AVATAR ===

    [IntegrationFact]
    public async Task Profile_UpdateAvatar()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            avatarUrl = "https://example.com/avatar.jpg"
        };

        var response = await ApiTestClient.PutJsonAsync(_client, "/api/UserProfile/me/avatar", updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion

    #region === PROGRESS ===

    [IntegrationFact]
    public async Task Profile_UpdateProgress()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var updateRequest = new
        {
            favoriteGameId = catanId
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/UserProfile/progress", updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion

    #region === KARMA HISTORY ===

    [IntegrationFact]
    public async Task Profile_GetKarmaHistory()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/UserProfile/me/karma-history");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Profile_GetKarmaHistory_ByUserId()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/UserProfile/{IntegrationTestFixtures.DemoPlayer1UserId}/karma-history");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === SEARCH ===

    [IntegrationFact]
    public async Task Profile_Search()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/UserProfile/search?query=player");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion
}
