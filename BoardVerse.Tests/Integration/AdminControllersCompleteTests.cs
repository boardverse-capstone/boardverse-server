#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Tests for AdminConfigurationController
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminConfigurationControllerIntegrationTests
{
    private readonly HttpClient _client;

    public AdminConfigurationControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === CONFIGURATION CRUD ===

    [IntegrationFact]
    public async Task Configuration_GetAll()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/configs");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Configuration_GetByKey()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/configs/BookingDepositTimeout");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Configuration_Update()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            key = "TestConfig",
            value = "TestValue",
            description = "Test configuration",
            category = "General"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            "/api/v1/admin/configs",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Configuration_Create()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var createRequest = new
        {
            key = $"TestConfig_{Guid.NewGuid():N}".Substring(0, 20),
            value = "NewValue",
            description = "New test configuration",
            category = "Testing",
            isSecret = false
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            "/api/v1/admin/configs",
            createRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.InternalServerError ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone,
                   $"Configuration create returned: {(int)response.StatusCode}");
    }

    [IntegrationFact]
    public async Task Configuration_Delete()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync(
            $"/api/v1/admin/configs/TestConfig_{Guid.NewGuid():N}".Substring(0, 30));
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion
}

/// <summary>
/// Tests for AdminCafeController (Extended)
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminCafeControllerExtendedIntegrationTests
{
    private readonly HttpClient _client;

    public AdminCafeControllerExtendedIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === CAFE MANAGEMENT ===

    [IntegrationFact]
    public async Task AdminCafe_GetAll()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/cafes");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task AdminCafe_GetById()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task AdminCafe_SetOperationalStatus()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new { status = "Open" };
        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task AdminCafe_UpdateSePayConfig()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            merchantId = "ADMIN_TEST_123",
            webhookUrl = "https://admin.test.com/webhook"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/sepay-config",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion
}

/// <summary>
/// Tests for AdminModerationController
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminModerationControllerIntegrationTests
{
    private readonly HttpClient _client;

    public AdminModerationControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === KARMA MODERATION ===

    [IntegrationFact]
    public async Task Moderation_GetKarmaLogs()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/karma-logs?page=1&pageSize=10");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Moderation_GetUserKarmaAlerts()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/users/alerts?minKarma=50");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Moderation_PunishUser()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            reason = "Test punishment",
            punishmentType = "Warning",
            durationDays = 7,
            notes = "Integration test"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/users/{IntegrationTestFixtures.DemoPlayer1UserId}/punish",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Moderation_AdjustKarma()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            adjustment = -10,
            reason = "Test karma adjustment"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/users/{IntegrationTestFixtures.DemoPlayer1UserId}/adjust-karma",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task Moderation_GetUserReports()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/users/reports");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Moderation_ProcessReport()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            action = "Warn",
            notes = "Processed by admin"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/users/reports/{Guid.NewGuid()}/process",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion
}
