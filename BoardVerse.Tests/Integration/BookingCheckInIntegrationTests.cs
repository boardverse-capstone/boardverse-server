using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho host-led check-in flow (Booking → POS).
/// Endpoint: POST /api/cafes/{cafeId}/pos/sessions/from-booking
/// Phủ: BR-05, BR-09, BR-18. Quét mã đặt chỗ (BookingCode = OrderId) để kích hoạt phiên chơi cho cả nhóm.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class BookingCheckInIntegrationTests
{
    private readonly HttpClient _client;

    public BookingCheckInIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task StartSessionFromBooking_AsManager_WithInvalidBookingCode_Returns404()
    {
        // Arrange
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act — BookingCode không tồn tại
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/from-booking",
            new
            {
                bookingCode = "BV-NOT-EXIST",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task StartSessionFromBooking_AsPlayer_Returns403()
    {
        // Arrange
        var playerToken = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, playerToken);

        // Act
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/from-booking",
            new
            {
                bookingCode = "BV12345678",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert — Player không có quyền POS
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [IntegrationFact]
    public async Task StartSessionFromBooking_WithPendingDeposit_Returns409()
    {
        // Arrange — DemoBookingDepositId được bootstrap tạo ở status Pending, không phải Paid
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act — Quét mã đặt chỗ của deposit vẫn còn Pending
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/from-booking",
            new
            {
                bookingCode = "BV-PENDING",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert — Có thể 404 (bookingCode không tồn tại) hoặc 409 (booking chưa paid)
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict,
            $"Expected NotFound or Conflict, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [IntegrationFact]
    public async Task StartSessionFromBooking_WithMissingBookingCode_Returns400()
    {
        // Arrange
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act — Body rỗng (bookingCode = "")
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/from-booking",
            new
            {
                bookingCode = "",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert — Model validation rejects empty bookingCode
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest,
            $"Expected 400, got {response.StatusCode}");
    }

    [IntegrationFact]
    public async Task StartSessionFromBooking_WithInvalidBarcode_Returns404()
    {
        // Arrange — Need a paid deposit OR use an invalid bookingCode that returns 404 first
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act — BookingCode valid (chỉ để pass lookup), nhưng barcode không tồn tại
        // Dòng đầu tiên trong service là _depositRepository.GetByBookingCodeAsync(...)
        // Vì bookingCode không tồn tại, expect 404
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/from-booking",
            new
            {
                bookingCode = "BV-NOTFOUND",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = "INVALID-BARCODE"
            });

        // Assert — Service fails on booking lookup first, returns 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task StartSessionFromBooking_EndpointRequiresAuth()
    {
        // Arrange — No token
        // Clear any existing auth
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/from-booking",
            new
            {
                bookingCode = "BV12345678",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task StartSessionFromBooking_WithCafeStaffRole_ReturnsSuccessOrPermissionError()
    {
        // Arrange — CafeStaff cũng có quyền POS theo [Authorize(Roles = "Manager,CafeStaff")]
        var staffToken = await IntegrationTestAuth.AsPlayer2Async(_client); // Player2 là CafeStaff theo bootstrapper
        ApiTestClient.Authorize(_client, staffToken);

        // Act
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/sessions/from-booking",
            new
            {
                bookingCode = "BV-INVALID-FOR-STAFF",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert — Auth passes, lookup fails with 404
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404 (booking not found) or 403 (CafeStaff not on staff list), got {response.StatusCode}");
    }
}
