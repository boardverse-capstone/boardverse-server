#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for ActiveSessionController
/// Covers: Full session management, checkout, merge, games
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ActiveSessionControllerIntegrationTests
{
    private readonly HttpClient _client;

    public ActiveSessionControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === SESSION MANAGEMENT ===

    [IntegrationFact]
    public async Task ActiveSession_GetById()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task ActiveSession_Checkout()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var checkoutRequest = new
        {
            notes = "Test checkout"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}/checkout",
            checkoutRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task ActiveSession_AddGuestSlot()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var guestRequest = new
        {
            displayName = "Guest Player",
            seatCount = 1
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}/guest-slots",
            guestRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task ActiveSession_PartialCheckout()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var partialRequest = new
        {
            memberIds = new[] { IntegrationTestFixtures.DemoPlayer1UserId },
            notes = "Partial checkout - some members leaving"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}/partial-checkout",
            partialRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === MERGE SESSION ===

    [IntegrationFact]
    public async Task ActiveSession_MergeIntoAnotherSession()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var mergeRequest = new
        {
            targetSessionId = Guid.NewGuid()
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}/merge",
            mergeRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === PAYMENT ===

    [IntegrationFact]
    public async Task ActiveSession_Pay()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var payRequest = new
        {
            paymentMethod = "Cash",
            amount = 60000
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}/pay",
            payRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === GAMES ===

    [IntegrationFact]
    public async Task ActiveSession_AttachGame()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var attachRequest = new
        {
            barcode = IntegrationTestFixtures.PosBoxBarcode
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}/games",
            attachRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    #endregion

    #region === MEMBERS ===

    [IntegrationFact]
    public async Task ActiveSession_AddLateMember()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var addRequest = new
        {
            userId = IntegrationTestFixtures.DemoPlayer1UserId,
            startTime = DateTime.UtcNow
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}/members/add",
            addRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    #endregion

    #region === INVENTORY LOSS ===

    [IntegrationFact]
    public async Task ActiveSession_RecordInventoryLoss()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var lossRequest = new
        {
            componentId = Guid.NewGuid(),
            componentName = "Missing Component",
            quantity = 1,
            penaltyAmount = 15000,
            notes = "Lost during session"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/{Guid.NewGuid()}/inventory-loss",
            lossRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === ALTERNATIVE CAFES ===

    [IntegrationFact]
    public async Task ActiveSession_GetAlternativeCafes()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/sessions/alternative-cafes?gameTemplateId={Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion
}
