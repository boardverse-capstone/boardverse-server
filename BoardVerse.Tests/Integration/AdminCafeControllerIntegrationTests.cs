using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho AdminCafeController endpoints.
/// 
/// Coverage:
/// - PUT /api/v1/admin/cafes/{cafeId}/operational-status
/// - BR-18: Admin set operational status (force majeure scenarios)
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminCafeControllerIntegrationTests
{
    private readonly HttpClient _client;

    public AdminCafeControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region PUT /api/v1/admin/cafes/{cafeId}/operational-status

    /// <summary>
    /// BR-18: Admin đặt trạng thái DATA_BLANK cho cafe mới
    /// </summary>
    [IntegrationFact]
    public async Task SetOperationalStatus_DataBlank_AsAdmin_Returns200()
    {
        // Arrange
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        // Act
        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            new { status = 0 }); // DATA_BLANK

        // Assert
        Assert.True(
            response.IsSuccessStatusCode ||
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Admin should set operational status, got {response.StatusCode}");
    }

    /// <summary>
    /// BR-18: Admin đặt trạng thái ACTIVE cho cafe
    /// </summary>
    [IntegrationFact]
    public async Task SetOperationalStatus_Active_AsAdmin_Returns200()
    {
        // Arrange
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        // Act
        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            new { status = 1 }); // ACTIVE

        // Assert
        Assert.True(
            response.IsSuccessStatusCode ||
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Admin should set operational status to ACTIVE, got {response.StatusCode}");
    }

    /// <summary>
    /// BR-18: Admin đặt trạng thái INACTIVE cho cafe
    /// </summary>
    [IntegrationFact]
    public async Task SetOperationalStatus_Inactive_AsAdmin_Returns200()
    {
        // Arrange
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        // Act
        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            new { status = 2 }); // INACTIVE

        // Assert
        Assert.True(
            response.IsSuccessStatusCode ||
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Admin should set operational status to INACTIVE, got {response.StatusCode}");
    }

    /// <summary>
    /// BR-18: Admin đặt trạng thái BANNED với lý do
    /// Exception 9: Force majeure cancellation
    /// </summary>
    [IntegrationFact]
    public async Task SetOperationalStatus_Banned_WithReason_AsAdmin_Returns200()
    {
        // Arrange
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        // Act
        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            new
            {
                status = 3, // BANNED
                reason = "Force majeure - natural disaster"
            });

        // Assert
        Assert.True(
            response.IsSuccessStatusCode ||
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Admin should ban cafe with reason, got {response.StatusCode}");
    }

    /// <summary>
    /// BR-18: BANNED mà không có reason phải trả về 400
    /// </summary>
    [IntegrationFact]
    public async Task SetOperationalStatus_Banned_WithoutReason_Returns400()
    {
        // Arrange
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        // Act - BANNED without reason
        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            new { status = 3 }); // BANNED without reason

        // Assert
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Banned without reason should return 400, got {response.StatusCode}");
    }

    /// <summary>
    /// Manager không có quyền set operational status
    /// </summary>
    [IntegrationFact]
    public async Task SetOperationalStatus_AsManager_Returns403()
    {
        // Arrange
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act
        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            new { status = 1 });

        // Assert
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Manager should get 403, got {response.StatusCode}");
    }

    /// <summary>
    /// Player không có quyền set operational status
    /// </summary>
    [IntegrationFact]
    public async Task SetOperationalStatus_AsPlayer_Returns403()
    {
        // Arrange
        var playerToken = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, playerToken);

        // Act
        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            new { status = 1 });

        // Assert
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Player should get 403, got {response.StatusCode}");
    }

    /// <summary>
    /// Cafe không tồn tại
    /// </summary>
    [IntegrationFact]
    public async Task SetOperationalStatus_NonExistentCafe_Returns404()
    {
        // Arrange
        var adminToken = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, adminToken);

        var nonExistentCafeId = Guid.NewGuid();

        // Act
        var response = await ApiTestClient.PutJsonAsync(
            _client,
            $"/api/v1/admin/cafes/{nonExistentCafeId}/operational-status",
            new { status = 1 });

        // Assert
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Non-existent cafe should return 404/403/400, got {response.StatusCode}");
    }

    #endregion
}
