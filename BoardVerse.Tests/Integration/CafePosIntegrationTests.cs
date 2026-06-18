using System.Net;
using BoardVerse.Core.Data;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class CafePosIntegrationTests
{
    private readonly HttpClient _client;

    public CafePosIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task GetTables_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/cafes/{DevSeedConstants.DemoCafeId}/pos/tables");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetBoxes_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var response = await _client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/pos/boxes?gameTemplateId={gameId}");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetBoxByBarcode_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        Assert.False(string.IsNullOrWhiteSpace(IntegrationTestFixtures.PosBoxBarcode));

        var response = await _client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/pos/boxes/by-barcode/{Uri.EscapeDataString(IntegrationTestFixtures.PosBoxBarcode)}");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetActiveSessions_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/pos/sessions/active");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task StartAndEndSession_AsManager_Returns201()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var startResponse = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/pos/sessions",
            new
            {
                cafeTableId = DevSeedConstants.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        startResponse.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);

        var sessionId = (await ApiTestClient.ReadApiResponseAsync<SessionStartedDto>(startResponse)).Data!.Id;

        var endResponse = await _client.PostAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/pos/sessions/{sessionId}/end",
            null);
        endResponse.EnsureSuccessStatusCode();
    }

    private sealed class SessionStartedDto
    {
        public Guid Id { get; set; }
    }
}
