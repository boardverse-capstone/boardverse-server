using System.Net;
using BoardVerse.Core.Data;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class UserProfileIntegrationTests
{
    private readonly HttpClient _client;

    public UserProfileIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task GetProfile_AsPlayer_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/userprofile");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task UpdateProfile_AsPlayer_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PutJsonAsync(_client, "/api/userprofile", new
        {
            displayName = "Demo Player One"
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task UpdateProgress_AsPlayer_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/userprofile/progress", new
        {
            gamesPlayed = 1
        });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task UpdateAvatar_AsPlayer_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PutJsonAsync(_client, "/api/userprofile/me/avatar", new
        {
            avatarUrl = "https://example.com/avatar.png"
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetLocation_AsPlayer_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/userprofile/me/location");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task PutLocation_AsPlayer_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PutJsonAsync(_client, "/api/userprofile/me/location", new
        {
            latitude = DevSeedConstants.DemoCafeLatitude,
            longitude = DevSeedConstants.DemoCafeLongitude,
            source = "Gps"
        });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetKarmaHistory_AsPlayer_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/userprofile/me/karma-history");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task CreateProfile_NewUser_Returns201Or409()
    {
        var email = ApiTestClient.UniqueEmail("profile");
        var username = ApiTestClient.UniqueUsername("profile");
        var password = "Profile@123";

        await ApiTestClient.PostJsonAsync(_client, "/api/auth/register", new
        {
            username,
            email,
            password
        });

        var token = await ApiTestClient.LoginAsync(_client, email, password);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/userprofile", new
        {
            displayName = "Integration Profile"
        });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.Created, HttpStatusCode.Conflict);
    }
}
