#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for DebugSePayController
/// Covers: Debug endpoints for SePay testing
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class DebugSePayControllerIntegrationTests
{
    private readonly HttpClient _client;

    public DebugSePayControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === DEBUG ENDPOINTS ===

    [IntegrationFact]
    public async Task DebugSePay_GetCheckout()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/debug/sepay/checkout?amount=50000");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task DebugSePay_GetHealth()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/debug/sepay/health");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.Forbidden);
    }

    [IntegrationFact]
    public async Task DebugSePay_CreateTestDeposit()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            userId = IntegrationTestFixtures.DemoPlayer1UserId,
            amount = 50000,
            cafeId = IntegrationTestFixtures.DemoCafeId
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/debug/sepay/test-deposit", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task DebugSePay_GetTestPage()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/debug/sepay/test-page?orderId=BV12345678");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion
}
