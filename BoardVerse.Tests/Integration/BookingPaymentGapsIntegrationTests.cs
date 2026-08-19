#nullable enable
using System.Net;
using System.Text.Json;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho 4 endpoint mới theo booking-payment-gaps.md:
/// - Task #8:  GET /api/bookings/{id}/session-status
/// - Task #12: PATCH /api/cafes/{id}/deposit-refund-policy
/// - Task #13: PUT /api/cafes/{id}/pricing-config
/// - Task #14: GET /api/bookings/cafe/{id} (Player view summary)
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class BookingPaymentGapsIntegrationTests
{
    private readonly HttpClient _client;

    public BookingPaymentGapsIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === TASK #14: GET /api/bookings/cafe/{id} cho Player ===

    [IntegrationFact]
    public async Task Task14_GetBookingsByCafe_AsPlayer_ReturnsSummaryFieldsOnly()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/bookings/cafe/{IntegrationTestFixtures.DemoCafeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);

        // Player view dùng QueryString không filter — trả về list summary rút gọn
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.NotEqual(JsonValueKind.Null, data.ValueKind);

        // Nếu data là array → Player view (summary)
        if (data.ValueKind == JsonValueKind.Array)
        {
            // Verify rằng chỉ có summary fields (KHÔNG có verificationQRCode/paymentRef)
            if (data.GetArrayLength() > 0)
            {
                var first = data[0];
                Assert.True(first.TryGetProperty("id", out _));
                Assert.True(first.TryGetProperty("scheduledStartTime", out _));
                Assert.True(first.TryGetProperty("scheduleEndTime", out _));
                Assert.True(first.TryGetProperty("playerQuantity", out _));
                // Sensitive fields KHÔNG được lộ cho Player
                Assert.False(first.TryGetProperty("verificationQRCode", out _));
                Assert.False(first.TryGetProperty("paymentRef", out _));
            }
        }
    }

    [IntegrationFact]
    public async Task Task14_GetBookingsByCafe_AsManager_ReturnsFullDto()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/bookings/cafe/{IntegrationTestFixtures.DemoCafeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region === TASK #12: PATCH /api/cafes/{id}/deposit-refund-policy ===

    [IntegrationFact]
    public async Task Task12_UpdateRefundPolicy_AsManager_ReturnsUpdatedPolicy()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new UpdateRefundPolicyRequestDto
        {
            Policy = Core.Enum.DepositRefundPolicy.Partial,
            PartialTiers = new List<RefundTierDto>
            {
                new() { MinHoursBeforeScheduled = 24, RefundPercent = 75 },
                new() { MinHoursBeforeScheduled = 12, RefundPercent = 50 },
                new() { MinHoursBeforeScheduled = 0, RefundPercent = 0 }
            }
        };

        var response = await ApiTestClient.PatchJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/deposit-refund-policy",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        // Policy là enum → có thể trả int hoặc string; check cả 2
        var policyElement = data.GetProperty("policy");
        if (policyElement.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal((int)Core.Enum.DepositRefundPolicy.Partial, policyElement.GetInt32());
        }
        else
        {
            Assert.Equal("Partial", policyElement.GetString());
        }
    }

    [IntegrationFact]
    public async Task Task12_UpdateRefundPolicy_PartialWithoutTiers_Returns400()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new UpdateRefundPolicyRequestDto
        {
            Policy = Core.Enum.DepositRefundPolicy.Partial,
            PartialTiers = null // Thiếu tiers cho Partial
        };

        var response = await ApiTestClient.PatchJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/deposit-refund-policy",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Task12_UpdateRefundPolicy_AsPlayer_Returns403()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new UpdateRefundPolicyRequestDto
        {
            Policy = Core.Enum.DepositRefundPolicy.Full
        };

        var response = await ApiTestClient.PatchJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/deposit-refund-policy",
            request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Task12_UpdateRefundPolicy_FullPolicy_NoTiersRequired()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new UpdateRefundPolicyRequestDto
        {
            Policy = Core.Enum.DepositRefundPolicy.Full
        };

        var response = await ApiTestClient.PatchJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/deposit-refund-policy",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region === TASK #13: PUT /api/cafes/{id}/pricing-config ===

    [IntegrationFact]
    public async Task Task13_UpdatePricingConfig_AsManager_ReturnsUpdated()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new UpdatePricingConfigRequestDto
        {
            BasePrice = 90000m,
            TieredBlockRate = 25000m,
            TieredBlockMinutes = 15
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pricing-config",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(90000m, data.GetProperty("basePrice").GetDecimal());
    }

    [IntegrationFact]
    public async Task Task13_UpdatePricingConfig_AsPlayer_Returns403()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new UpdatePricingConfigRequestDto
        {
            BasePrice = 50000m
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pricing-config",
            request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region === TASK #8: GET /api/bookings/{id}/session-status ===

    [IntegrationFact]
    public async Task Task8_GetSessionStatus_NoBookingId_Returns404()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var fakeBookingId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/bookings/{fakeBookingId}/session-status");

        // Không tìm thấy booking → 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Task8_GetSessionStatus_WithoutToken_Returns401()
    {
        // Không Authorize → 401
        var fakeBookingId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/bookings/{fakeBookingId}/session-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion
}
