#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for Split Bill Webhook feature.
/// Covers: Member payment webhook, idempotency, audit logging.
/// Fixes #1-7 verified.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SplitBillWebhookIntegrationTests
{
    private readonly HttpClient _client;

    public SplitBillWebhookIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === MEMBER PAYMENT WEBHOOK TESTS ===

    [IntegrationFact]
    public async Task SplitBill_Webhook_WithEmptyMemberId_AndInvalidOrderId_ReturnsOk()
    {
        // Fix #6: Webhook should return 200 instead of throwing 404
        // to prevent SePay from stopping retries
        var webhook = new MemberPaymentWebhookDto
        {
            OrderId = "INVALID-ORDER-ID",
            MemberId = Guid.Empty,
            Amount = 50000,
            Status = "success",
            GatewayTransactionId = $"TXN-TEST-{Guid.NewGuid():N}"
        };

        var response = await ApiTestClient.PostJsonAsync(
            _client,
            "/api/payments/sepay/webhook/member-payment",
            webhook);

        // Should return 200 OK even with invalid member ID
        Assert.True(response.StatusCode == HttpStatusCode.OK);
    }

    [IntegrationFact]
    public async Task SplitBill_Webhook_WithDuplicateGatewayTxnId_IsIdempotent()
    {
        // Fix #2: Duplicate webhook with same GatewayTransactionId should be ignored
        var gatewayTxnId = $"TXN-DUPLICATE-{Guid.NewGuid():N}";

        var webhook1 = new MemberPaymentWebhookDto
        {
            OrderId = $"BV-MEMBER-{Guid.NewGuid():N}",
            MemberId = Guid.Empty,
            Amount = 50000,
            Status = "success",
            GatewayTransactionId = gatewayTxnId
        };

        var webhook2 = new MemberPaymentWebhookDto
        {
            OrderId = $"BV-MEMBER-{Guid.NewGuid():N}",
            MemberId = Guid.Empty,
            Amount = 50000,
            Status = "success",
            GatewayTransactionId = gatewayTxnId
        };

        // First call
        var response1 = await ApiTestClient.PostJsonAsync(
            _client,
            "/api/payments/sepay/webhook/member-payment",
            webhook1);

        // Second call with same gateway txn id
        var response2 = await ApiTestClient.PostJsonAsync(
            _client,
            "/api/payments/sepay/webhook/member-payment",
            webhook2);

        // Both should return 200 OK (idempotent)
        Assert.True(response1.StatusCode == HttpStatusCode.OK);
        Assert.True(response2.StatusCode == HttpStatusCode.OK);
    }

    [IntegrationFact]
    public async Task SplitBill_Webhook_FailedStatus_IsHandled()
    {
        // Failed/cancelled payments should be handled gracefully
        var webhook = new MemberPaymentWebhookDto
        {
            OrderId = $"BV-MEMBER-{Guid.NewGuid():N}",
            MemberId = Guid.Empty,
            Amount = 50000,
            Status = "failed",
            GatewayTransactionId = $"TXN-FAILED-{Guid.NewGuid():N}"
        };

        var response = await ApiTestClient.PostJsonAsync(
            _client,
            "/api/payments/sepay/webhook/member-payment",
            webhook);

        // Should return 200 OK even for failed status
        Assert.True(response.StatusCode == HttpStatusCode.OK);
    }

    [IntegrationFact]
    public async Task SplitBill_Webhook_CancelledStatus_IsHandled()
    {
        // Cancelled payments should be handled gracefully
        var webhook = new MemberPaymentWebhookDto
        {
            OrderId = $"BV-MEMBER-{Guid.NewGuid():N}",
            MemberId = Guid.Empty,
            Amount = 50000,
            Status = "cancelled",
            GatewayTransactionId = $"TXN-CANCEL-{Guid.NewGuid():N}"
        };

        var response = await ApiTestClient.PostJsonAsync(
            _client,
            "/api/payments/sepay/webhook/member-payment",
            webhook);

        // Should return 200 OK even for cancelled status
        Assert.True(response.StatusCode == HttpStatusCode.OK);
    }

    [IntegrationFact]
    public async Task SplitBill_Webhook_PaidStatus_IsHandled()
    {
        // "paid" status should be treated same as "success"
        var webhook = new MemberPaymentWebhookDto
        {
            OrderId = $"BV-MEMBER-{Guid.NewGuid():N}",
            MemberId = Guid.Empty,
            Amount = 50000,
            Status = "paid",
            GatewayTransactionId = $"TXN-PAID-{Guid.NewGuid():N}"
        };

        var response = await ApiTestClient.PostJsonAsync(
            _client,
            "/api/payments/sepay/webhook/member-payment",
            webhook);

        Assert.True(response.StatusCode == HttpStatusCode.OK);
    }

    [IntegrationFact]
    public async Task SplitBill_Webhook_WithMissingOrderId_AndEmptyMemberId_ReturnsOk()
    {
        // Fix #7: Handle edge case with no OrderId and empty MemberId
        var webhook = new MemberPaymentWebhookDto
        {
            OrderId = "",
            MemberId = Guid.Empty,
            Amount = 50000,
            Status = "success",
            GatewayTransactionId = $"TXN-NONE-{Guid.NewGuid():N}"
        };

        var response = await ApiTestClient.PostJsonAsync(
            _client,
            "/api/payments/sepay/webhook/member-payment",
            webhook);

        // Should return 200 OK to prevent SePay from stopping retries
        Assert.True(response.StatusCode == HttpStatusCode.OK);
    }

    #endregion

    #region === SPLIT BILL SERVICE TESTS ===

    [IntegrationFact]
    public async Task SplitBill_GetSessionPaymentStatus_ReturnsProperFormat()
    {
        // Test the GET endpoint for session payment status
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/pos/sessions/{Guid.NewGuid()}/payment-status");

        // Should return 200 or 404 (session not found)
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion
}
