#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for SePayAccountController
/// Covers: CRUD operations for SePay accounts
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SePayAccountControllerIntegrationTests
{
    private readonly HttpClient _client;

    public SePayAccountControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === GET ===

    [IntegrationFact]
    public async Task SePayAccount_GetById()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/sepay-accounts/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task SePayAccount_GetMasterAccount()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/sepay-accounts/master");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task SePayAccount_GetMyCafeAccount()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/sepay-accounts/my-cafe");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === CREATE ===

    [IntegrationFact]
    public async Task SePayAccount_Create()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var createRequest = new
        {
            provider = "SePay",
            accountHolder = "BoardVerse Test",
            bankCode = "TPBANK",
            accountNumber = "1234567890",
            webhookUrl = "https://test.example.com/webhook"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/sepay-accounts", createRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === UPDATE ===

    [IntegrationFact]
    public async Task SePayAccount_Update()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            accountHolder = "Updated Account Holder",
            webhookUrl = "https://updated.example.com/webhook"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/sepay-accounts/{Guid.NewGuid()}",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task SePayAccount_UpdateMyCafeAccount()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            merchantId = "TEST_MERCHANT_123",
            webhookUrl = "https://cafe.example.com/webhook"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            "/api/v1/sepay-accounts/my-cafe",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === DELETE ===

    [IntegrationFact]
    public async Task SePayAccount_Delete()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync($"/api/v1/sepay-accounts/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === ENVIRONMENT ===

    [IntegrationFact]
    public async Task SePayAccount_SetEnvironment()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var envRequest = new { environment = "Sandbox" };
        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/sepay-accounts/{Guid.NewGuid()}/environment",
            envRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task SePayAccount_SetMyCafeEnvironment()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var envRequest = new { environment = "Production" };
        var response = await ApiTestClient.PutJsonAsync(_client,
            "/api/v1/sepay-accounts/my-cafe/environment",
            envRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion
}
