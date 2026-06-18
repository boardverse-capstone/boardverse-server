using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class HealthIntegrationTests
{
    private readonly HttpClient _client;

    public HealthIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task Ping_Returns200()
    {
        var response = await _client.GetAsync("/api/health/ping");
        response.EnsureSuccessStatusCode();
        var body = await ApiTestClient.ReadApiResponseAsync<object>(response);
        Assert.Equal(200, body.StatusCode);
    }

    [IntegrationFact]
    public async Task Status_Returns200()
    {
        var response = await _client.GetAsync("/api/health/status");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task DbInfo_ReturnsUserCount()
    {
        var response = await _client.GetAsync("/api/health/db-info");
        response.EnsureSuccessStatusCode();
        var body = await ApiTestClient.ReadApiResponseAsync<DbInfoDto>(response);
        Assert.True(body.Data!.UserCount >= 0);
    }

    private sealed class DbInfoDto
    {
        public string Status { get; set; } = string.Empty;
        public int UserCount { get; set; }
    }
}

[Collection(IntegrationTestCollection.Name)]
public class ProtectedIntegrationTests
{
    private readonly HttpClient _client;

    public ProtectedIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task Secret_WithoutToken_Returns401()
    {
        ApiTestClient.ClearAuth(_client);
        var response = await _client.GetAsync("/api/protected/secret");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Secret_WithPlayerToken_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/protected/secret");
        response.EnsureSuccessStatusCode();
    }
}
