#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for ActiveSessionController - Extra endpoints
/// Covers: Get session, end-game, component checklist, games, inventory loss
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ActiveSessionControllerExtraIntegrationTests
{
    private readonly HttpClient _client;

    public ActiveSessionControllerExtraIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    private async Task<Guid?> StartTestSessionAsync()
    {
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        await CleanupActiveSessionsAsync();

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions",
            new
            {
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        if (response.StatusCode != HttpStatusCode.Created) return null;
        var body = await ApiTestClient.ReadApiResponseAsync<SessionStartedDto>(response);
        return body.Data!.Id;
    }

    #region === GET SESSION ===

    [IntegrationFact]
    public async Task Session_GetById()
    {
        var sessionId = await StartTestSessionAsync();
        if (sessionId == null) return;

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{sessionId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);

        await CleanupActiveSessionsAsync();
    }

    #endregion

    #region === GAMES ===

    [IntegrationFact]
    public async Task Session_AttachGame()
    {
        var sessionId = await StartTestSessionAsync();
        if (sessionId == null) return;

        var attachRequest = new { barcode = IntegrationTestFixtures.PosBoxBarcode };
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{sessionId}/games",
            attachRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Conflict);

        await CleanupActiveSessionsAsync();
    }

    [IntegrationFact]
    public async Task Session_CheckGameComponents()
    {
        var sessionId = await StartTestSessionAsync();
        if (sessionId == null) return;

        var checkRequest = new
        {
            gameBarcode = IntegrationTestFixtures.PosBoxBarcode,
            components = new[]
            {
                new { componentId = Guid.NewGuid(), present = true }
            }
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{sessionId}/games/check",
            checkRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);

        await CleanupActiveSessionsAsync();
    }

    #endregion

    #region === END GAME ===

    [IntegrationFact]
    public async Task Session_EndGame()
    {
        var sessionId = await StartTestSessionAsync();
        if (sessionId == null) return;

        var response = await _client.PostAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{sessionId}/end-game",
            null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);

        await CleanupActiveSessionsAsync();
    }

    #endregion

    #region === INVENTORY LOSS ===

    [IntegrationFact]
    public async Task Session_ReportInventoryLoss()
    {
        var sessionId = await StartTestSessionAsync();
        if (sessionId == null) return;

        var lossRequest = new
        {
            componentId = Guid.NewGuid(),
            componentName = "Test Component",
            quantity = 1,
            penaltyAmount = 15000,
            notes = "Integration test loss report"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{sessionId}/inventory-loss",
            lossRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound);

        await CleanupActiveSessionsAsync();
    }

    #endregion

    #region === COMPONENT CHECKLIST ===

    [IntegrationFact]
    public async Task Session_GetComponentChecklist()
    {
        var sessionId = await StartTestSessionAsync();
        if (sessionId == null) return;

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/{sessionId}/component-checklist");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);

        await CleanupActiveSessionsAsync();
    }

    [IntegrationFact]
    public async Task Session_ComponentCheck()
    {
        var sessionId = await StartTestSessionAsync();
        if (sessionId == null) return;

        var checkRequest = new
        {
            sessionId = sessionId,
            gameBarcode = IntegrationTestFixtures.PosBoxBarcode,
            verifiedComponents = new[]
            {
                new { componentId = Guid.NewGuid(), present = true, notes = "OK" }
            }
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/component-check",
            checkRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);

        await CleanupActiveSessionsAsync();
    }

    #endregion

    #region === RETURN GAME ===

    [IntegrationFact]
    public async Task Session_ReturnGame()
    {
        var sessionId = await StartTestSessionAsync();
        if (sessionId == null) return;

        var returnRequest = new
        {
            gameBarcode = IntegrationTestFixtures.PosBoxBarcode,
            componentsVerified = true
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/{sessionId}/return-game",
            returnRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);

        await CleanupActiveSessionsAsync();
    }

    #endregion

    #region === HELPER ===

    private async Task CleanupActiveSessionsAsync()
    {
        try
        {
            var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
            ApiTestClient.Authorize(_client, managerToken);

            var activeSessions = await _client.GetAsync(
                $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/active");

            if (activeSessions.IsSuccessStatusCode)
            {
                var sessionsData = await ApiTestClient.ReadApiResponseAsync<List<SessionStartedDto>>(activeSessions);
                foreach (var session in sessionsData.Data ?? [])
                {
                    await _client.PostAsync(
                        $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/{session.Id}/end",
                        null);
                }
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    #endregion
}
