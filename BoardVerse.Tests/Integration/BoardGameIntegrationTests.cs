using BoardVerse.Core.Enum;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class BoardGameIntegrationTests
{
    private readonly HttpClient _client;

    public BoardGameIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task GetCategories_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/board-games/categories");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task SearchGames_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/board-games?search=catan&pageSize=5");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetGameById_Returns200()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var response = await _client.GetAsync($"/api/v1/board-games/{gameId}");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetGameDetails_Returns200()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var response = await _client.GetAsync($"/api/v1/board-games/{gameId}/details");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task PlayConfiguration_Catan_ReturnsModes()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var response = await _client.GetAsync($"/api/v1/board-games/{gameId}/play-configuration");
        response.EnsureSuccessStatusCode();

        var body = await ApiTestClient.ReadApiResponseAsync<PlayConfigDto>(response);
        Assert.True(body.Data!.MinPlayers >= 1);
        Assert.NotEmpty(body.Data.AvailablePlayModes);
    }

    [IntegrationFact]
    public async Task PlayNavigation_GroupMode_Returns200()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var response = await ApiTestClient.PostJsonAsync(_client, $"/api/v1/board-games/{gameId}/play-navigation", new
        {
            playMode = PlayerPlayMode.Group
        });

        response.EnsureSuccessStatusCode();
    }

    private sealed class PlayConfigDto
    {
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public List<string> AvailablePlayModes { get; set; } = [];
    }
}

[Collection(IntegrationTestCollection.Name)]
public class MasterGameIntegrationTests
{
    private readonly HttpClient _client;

    public MasterGameIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task ListMasterGames_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/master-games?searchTerm=catan&cafeId={IntegrationTestFixtures.DemoCafeId}&pageSize=5");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetMasterGameById_AsManager_Returns200()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/master-games/{gameId}?cafeId={IntegrationTestFixtures.DemoCafeId}");
        response.EnsureSuccessStatusCode();
    }
}
