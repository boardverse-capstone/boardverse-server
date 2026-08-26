using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// W-06: Integration tests cho AdminSettlementController list endpoints mới
/// (GET /api/v1/admin/settlements và GET /api/v1/admin/settlements/failed).
/// Cho phép admin tìm SettlementId hợp lệ để retry/override mà không cần nhập UUID thủ công.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminSettlementListTests
{
    private readonly HttpClient _client;

    public AdminSettlementListTests(BoardVerseWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region /settlements (list mọi status)

    [IntegrationFact]
    public async Task GetSettlements_AsAdmin_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/settlements?pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetSettlements_WithStatusFilter_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/settlements?status=Failed&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetSettlements_WithInvalidStatus_Returns400()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/settlements?status=NotAValidStatus");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetSettlements_AsPlayer_Returns403()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/settlements");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetSettlements_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/admin/settlements");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region /settlements/failed

    [IntegrationFact]
    public async Task GetFailedSettlements_AsAdmin_Returns200()
    {
        // W-06: Endpoint chính cho admin tìm SettlementId để retry/override.
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/settlements/failed?pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetFailedSettlements_WithCafeIdFilter_Returns200()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/admin/settlements/failed?cafeId={Guid.NewGuid()}&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetFailedSettlements_WithDateRange_Returns200()
    {
        var from = DateTime.UtcNow.AddDays(-30).ToString("o");
        var to = DateTime.UtcNow.ToString("o");
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/admin/settlements/failed?fromUtc={Uri.EscapeDataString(from)}&toUtc={Uri.EscapeDataString(to)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetFailedSettlements_AsPlayer_Returns403()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/settlements/failed");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetFailedSettlements_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/admin/settlements/failed");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task GetFailedSettlements_PaginationBoundsClamped_Returns200()
    {
        // PaginationParams clamps PageSize > 100 → 100, PageNumber < 1 → 1.
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            "/api/v1/admin/settlements/failed?pageNumber=0&pageSize=999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion
}
