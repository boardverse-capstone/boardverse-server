using BoardVerse.Core.Data;
using BoardVerse.Core.Enum;
using BoardVerse.Tests.Integration.Infrastructure;
using System.Net;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class CafeInventoryIntegrationTests
{
    private readonly HttpClient _client;

    public CafeInventoryIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task GetInventory_Public_Returns200()
    {
        var response = await _client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory?pageSize=20");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetInventoryItem_Public_Returns200()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var snapshot = await IntegrationCatalog.GetDemoCafeInventoryAsync(_client, gameId);

        var response = await _client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory/{snapshot.InventoryId}");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task GetDeletedInventory_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory/deleted?pageSize=10");
        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task UpdateInventory_AsManager_Returns200()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var snapshot = await IntegrationCatalog.GetDemoCafeInventoryAsync(_client, gameId);
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory/{snapshot.InventoryId}",
            new { boxQuantity = 2 });

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task SyncPenalties_AsManager_Returns200()
    {
        var gameId = await IntegrationCatalog.GetCatanGameIdAsync(_client);
        var snapshot = await IntegrationCatalog.GetDemoCafeInventoryAsync(_client, gameId);
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory/{snapshot.InventoryId}/sync-penalties",
            null);

        response.EnsureSuccessStatusCode();
    }

    [IntegrationFact]
    public async Task AddInventory_NewGame_AsManager_Returns201Or409()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var gameId = await IntegrationCatalog.GetMasterGameIdAsync(_client, "ticket");

        var response = await ApiTestClient.PostJsonAsync(_client, $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory", new
        {
            gameTemplateId = gameId,
            boxQuantity = 1,
            status = CafeGameInventoryStatus.Available
        });

        await ApiTestClient.AssertStatusOneOfAsync(response, HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task SoftDeleteAndRestoreInventory_AsManager_Returns200()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var gameId = await IntegrationCatalog.GetMasterGameIdAsync(_client, "azul");
        var inventoryId = await IntegrationCatalog.GetOrCreateCafeInventoryIdAsync(
            _client,
            DevSeedConstants.DemoCafeId,
            gameId);

        var deleteResponse = await ApiTestClient.DeleteAsync(
            _client,
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory/{inventoryId}");
        deleteResponse.EnsureSuccessStatusCode();

        var restoreResponse = await _client.PostAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory/{inventoryId}/restore",
            null);
        restoreResponse.EnsureSuccessStatusCode();
    }
}
