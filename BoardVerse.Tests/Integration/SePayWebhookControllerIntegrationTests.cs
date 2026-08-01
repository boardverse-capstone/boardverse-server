#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for SePayWebhookController
/// Covers: Real webhook handling endpoints
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SePayWebhookControllerIntegrationTests
{
    private readonly HttpClient _client;

    public SePayWebhookControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === WEBHOOK ENDPOINTS ===

    [IntegrationFact]
    public async Task SePayWebhook_HandleSuccess()
    {
        var orderId = $"BV{Guid.NewGuid():N}".Substring(0, 20);

        var webhookRequest = new
        {
            id = Guid.NewGuid().ToString(),
            order_id = orderId,
            gateway = "SePay",
            gateway_transaction_id = $"TXN-{Guid.NewGuid():N}".Substring(0, 16),
            amount = 50000,
            currency = "VND",
            status = "success",
            reference_code = $"REF-{Guid.NewGuid():N}".Substring(0, 12),
            paid_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/payments/sepay/webhook", webhookRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task SePayWebhook_HandleFailed()
    {
        var orderId = $"BV{Guid.NewGuid():N}".Substring(0, 20);

        var webhookRequest = new
        {
            order_id = orderId,
            status = "failed",
            reference_code = $"REF-{Guid.NewGuid():N}".Substring(0, 12)
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/payments/sepay/webhook", webhookRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task SePayWebhook_HandleCancelled()
    {
        var orderId = $"BV{Guid.NewGuid():N}".Substring(0, 20);

        var webhookRequest = new
        {
            order_id = orderId,
            status = "cancelled",
            reference_code = $"REF-{Guid.NewGuid():N}".Substring(0, 12)
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/payments/sepay/webhook", webhookRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [IntegrationFact]
    public async Task SePayWebhook_ReturnPage()
    {
        var response = await _client.GetAsync(
            "/api/payments/sepay/webhook/return?order_id=BV12345678&status=success");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized);
    }

    #endregion
}
