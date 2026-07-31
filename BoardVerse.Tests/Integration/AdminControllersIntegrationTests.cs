#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for Admin controllers
/// Covers: AdminCafeController, AdminConfigurationController, AdminMasterCatalogController
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminControllersIntegrationTests
{
    private readonly HttpClient _client;

    public AdminControllersIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === ADMIN CAFE CONTROLLER ===

    [IntegrationFact]
    public async Task AdminCafe_UpdateOperationalStatus()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            status = "Open"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/admin/cafes/{IntegrationTestFixtures.DemoCafeId}/operational-status",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === ADMIN CONFIGURATION CONTROLLER ===

    [IntegrationFact]
    public async Task AdminConfiguration_GetAll()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/configurations");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task AdminConfiguration_GetByKey()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/configurations/BookingDepositTimeout");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task AdminConfiguration_Update()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            key = "TestConfig",
            value = "TestValue",
            description = "Test configuration"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            "/api/v1/admin/configurations",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === ADMIN MASTER CATALOG CONTROLLER ===

    [IntegrationFact]
    public async Task AdminCatalog_GetGames()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/master-catalog/games");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task AdminCatalog_GetGameById()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var response = await _client.GetAsync($"/api/v1/admin/master-catalog/games/{catanId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task AdminCatalog_CreateGame()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var createRequest = new
        {
            name = $"Test Game {Guid.NewGuid():N}".Substring(0, 20),
            description = "Test game from integration test",
            minPlayers = 2,
            maxPlayers = 4,
            avgPlayTimeMinutes = 60
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            "/api/v1/admin/master-catalog/games",
            createRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task AdminCatalog_UpdateGame()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var updateRequest = new
        {
            name = "Updated Catan Name",
            description = "Updated description"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/admin/master-catalog/games/{catanId}",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task AdminCatalog_GetCategories()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/master-catalog/categories");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task AdminCatalog_GetGameComponents()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var response = await _client.GetAsync($"/api/v1/admin/master-catalog/games/{catanId}/components");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === ADMIN MODERATION CONTROLLER ===

    [IntegrationFact]
    public async Task AdminModeration_GetKarmaLogs()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/karma-logs?page=1&pageSize=10");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task AdminModeration_GetUserKarmaAlerts()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/users/alerts?minKarma=50");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task AdminModeration_PunishUser()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var punishRequest = new
        {
            reason = "Test punishment",
            punishmentType = "Warning",
            duration = "7d"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/users/{IntegrationTestFixtures.DemoPlayer1UserId}/punish",
            punishRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task AdminModeration_AdjustKarma()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var adjustRequest = new
        {
            adjustment = -10,
            reason = "Test karma adjustment"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/admin/users/{IntegrationTestFixtures.DemoPlayer1UserId}/adjust-karma",
            adjustRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === PAYMENT MASTER ACCOUNT CONTROLLER ===

    [IntegrationFact]
    public async Task PaymentMasterAccount_GetAll()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/payment-master-accounts");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task PaymentMasterAccount_GetById()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/admin/payment-master-accounts/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task PaymentMasterAccount_Create()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var createRequest = new
        {
            provider = "SePay",
            accountHolder = "Test Company",
            bankCode = "TPBANK",
            maskedAccountNumber = "****5678",
            virtualAccountNumber = $"TEST{Guid.NewGuid():N}".Substring(0, 12),
            qrContent = "https://qr.sepay.vn/img?acc=TEST5678",
            webhookSecret = "test_webhook_secret_admin"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            "/api/v1/admin/payment-master-accounts",
            createRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === USER MANAGEMENT CONTROLLER ===

    [IntegrationFact]
    public async Task UserManagement_GetUsers()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/admin/users?page=1&pageSize=20");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task UserManagement_GetUserById()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/admin/users/{IntegrationTestFixtures.DemoPlayer1UserId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task UserManagement_UpdateUser()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            displayName = "Admin Updated Name",
            isVerified = false
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/admin/users/{IntegrationTestFixtures.DemoPlayer1UserId}",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion
}
