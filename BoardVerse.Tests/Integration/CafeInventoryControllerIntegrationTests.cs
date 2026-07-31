#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for CafeInventoryController
/// Covers: Inventory management, restore, sync
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CafeInventoryControllerIntegrationTests
{
    private readonly HttpClient _client;

    public CafeInventoryControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === GET ===

    [IntegrationFact]
    public async Task CafeInventory_GetAll()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task CafeInventory_GetById()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory/{IntegrationTestFixtures.DemoCatanInventoryId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task CafeInventory_GetDeleted()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory/deleted");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    #endregion

    #region === RESTORE ===

    [IntegrationFact]
    public async Task CafeInventory_Restore()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory/{Guid.NewGuid()}/restore", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === SYNC ===

    [IntegrationFact]
    public async Task CafeInventory_SyncPenalties()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var syncRequest = new
        {
            penaltyPolicy = "Standard",
            updateExisting = true
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory/{Guid.NewGuid()}/sync-penalties",
            syncRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task CafeInventory_SyncBoxes()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var syncRequest = new
        {
            boxes = new[]
            {
                new { barcode = "TEST123", condition = "Good", status = "Available" }
            }
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/cafes/{IntegrationTestFixtures.DemoCafeId}/inventory/{Guid.NewGuid()}/sync-boxes",
            syncRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion
}
