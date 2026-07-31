#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for PaymentController
/// Covers: Deposit, QR regenerate, Manual confirm, Session payment
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PaymentControllerIntegrationTests
{
    private readonly HttpClient _client;

    public PaymentControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === BOOKING DEPOSIT ===

    [IntegrationFact]
    public async Task Payment_CreateBookingDeposit()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            cafeId = IntegrationTestFixtures.DemoCafeId,
            amount = 50000,
            paymentMethod = "SePay",
            bookingGroupCode = $"GRP-{Guid.NewGuid():N}".Substring(0, 16)
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/payments/booking-deposit", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task Payment_GetBookingDepositById()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/payments/booking-deposit/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Payment_GetBookingDepositByOrderId()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/payments/booking-deposit/by-order/BV{Guid.NewGuid():N}".Substring(0, 20));
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Payment_RegenerateDepositQR()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/payments/booking-deposit/{Guid.NewGuid()}/regenerate-qr",
            new { paymentMethod = "SePay" });
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task Payment_RefundDeposit()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            depositId = Guid.NewGuid(),
            reason = "Test refund",
            refundAmount = 50000
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/payments/booking-deposit/refund", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === SESSION PAYMENT ===

    [IntegrationFact]
    public async Task Payment_CreateSessionPayment()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            sessionId = Guid.NewGuid(),
            amount = 60000,
            paymentMethod = "SePay"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/payments/session-payment", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    [IntegrationFact]
    public async Task Payment_RegenerateSessionQR()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/payments/session-payment/{Guid.NewGuid()}/regenerate-qr",
            new { paymentMethod = "SePay" });
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict);
    }

    #endregion

    #region === MANUAL CONFIRM ===

    [IntegrationFact]
    public async Task Payment_ManualConfirm()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            depositId = Guid.NewGuid(),
            confirmedAmount = 50000,
            notes = "Manual confirmation for test"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/payments/manual-confirm", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion
}
