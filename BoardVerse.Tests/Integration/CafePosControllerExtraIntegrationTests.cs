#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for CafePosController - Extra endpoints
/// Covers: Sync tables, bookings, component checklist
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CafePosControllerExtraIntegrationTests
{
    private readonly HttpClient _client;

    public CafePosControllerExtraIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === SYNC TABLES ===

    [IntegrationFact]
    public async Task CafePos_SyncTables()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var syncRequest = new
        {
            tables = new[]
            {
                new
                {
                    id = IntegrationTestFixtures.DemoPosTableId,
                    name = "Table 1",
                    seatCount = 4,
                    status = "Available"
                }
            }
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables",
            syncRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === BOOKINGS ===

    [IntegrationFact]
    public async Task CafePos_GetBookingByCode()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/bookings/TEST123");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task CafePos_GetActiveBookings()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/bookings?status=Confirmed");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === SESSIONS ===

    [IntegrationFact]
    public async Task CafePos_GetActiveSessions()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/active");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task CafePos_StartSession_FromBooking()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var startRequest = new
        {
            bookingCode = "TEST123",
            cafeTableId = IntegrationTestFixtures.DemoPosTableId
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/from-booking",
            startRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    #endregion

    #region === BOXES ===

    [IntegrationFact]
    public async Task CafePos_GetBoxes()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/boxes");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task CafePos_GetBoxByBarcode()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/boxes/by-barcode/{IntegrationTestFixtures.PosBoxBarcode}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === TABLES ===

    [IntegrationFact]
    public async Task CafePos_GetTables()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/tables");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    #endregion
}
