using System.Net;
using System.Net.Http.Json;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// BR-REFUND-07: Integration tests cho AdminReservationController.OverrideRefund endpoint.
/// Endpoint: POST /api/v1/admin/reservations/{reservationId}/override-refund
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminReservationOverrideRefundTests
{
    private readonly HttpClient _client;

    public AdminReservationOverrideRefundTests(BoardVerseWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [IntegrationFact]
    public async Task OverrideRefund_MissingIdempotencyKey_Returns400()
    {
        // BR § XVII.1: idempotency key bắt buộc cho mọi admin override.
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new AdminOverrideRefundRequestDto { RefundAmountBvc = 100, Reason = "Test reason" };
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/reservations/{Guid.NewGuid()}/override-refund",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task OverrideRefund_NonexistentReservation_Returns404()
    {
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new AdminOverrideRefundRequestDto { RefundAmountBvc = 100, Reason = "Test reason" };
        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", $"test-{Guid.NewGuid()}");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/reservations/{Guid.NewGuid()}/override-refund",
            request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task OverrideRefund_ReasonTooShort_Returns400()
    {
        // DTO validation: [StringLength(2000, MinimumLength = 5)]
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new AdminOverrideRefundRequestDto { RefundAmountBvc = 100, Reason = "abc" }; // < 5 ký tự
        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", $"test-{Guid.NewGuid()}");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/reservations/{Guid.NewGuid()}/override-refund",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task OverrideRefund_NegativeRefundAmount_Returns400()
    {
        // DTO validation: [Range(0, 10_000_000)]
        var token = await IntegrationTestAuth.AsAdminAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new AdminOverrideRefundRequestDto { RefundAmountBvc = -100, Reason = "Negative not allowed" };
        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", $"test-{Guid.NewGuid()}");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/reservations/{Guid.NewGuid()}/override-refund",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task OverrideRefund_AsPlayer_Returns403()
    {
        // [Authorize(Roles = "Admin")] — chỉ admin mới được.
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new AdminOverrideRefundRequestDto { RefundAmountBvc = 100, Reason = "Should be denied" };
        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", $"test-{Guid.NewGuid()}");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/reservations/{Guid.NewGuid()}/override-refund",
            request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [IntegrationFact]
    public async Task OverrideRefund_Unauthenticated_Returns401()
    {
        // Không có token.
        _client.DefaultRequestHeaders.Authorization = null;

        var request = new AdminOverrideRefundRequestDto { RefundAmountBvc = 100, Reason = "Test reason" };
        _client.DefaultRequestHeaders.Remove("Idempotency-Key");
        _client.DefaultRequestHeaders.Add("Idempotency-Key", $"test-{Guid.NewGuid()}");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/reservations/{Guid.NewGuid()}/override-refund",
            request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}