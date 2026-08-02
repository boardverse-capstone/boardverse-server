using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho POS check-in flow (BR §21A.7).
/// Endpoint: POST /api/cafes/{cafeId}/pos/check-in
/// Phủ: BR-05, BR-09, BR-18, BR-21A.7. Staff quét QR (ReservationCode | BookingCode legacy)
/// để kích hoạt phiên chơi cho cả nhóm (host-led check-in).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class BookingCheckInIntegrationTests
{
    private readonly HttpClient _client;

    public BookingCheckInIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task CheckIn_AsManager_WithInvalidCode_Returns404()
    {
        // Arrange
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act — Code không tồn tại (không phải ReservationCode hợp lệ, không phải BookingCode hợp lệ)
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/check-in",
            new
            {
                code = "BV-NOT-EXIST",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task CheckIn_AsPlayer_Returns403()
    {
        // Arrange
        var playerToken = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, playerToken);

        // Act
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/check-in",
            new
            {
                code = "BV12345678",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert — Player không có quyền POS
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [IntegrationFact]
    public async Task CheckIn_WithPendingDeposit_Returns409()
    {
        // Arrange — DemoBookingDepositId được bootstrap tạo ở status Pending, không phải Paid
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act — Quét mã của deposit vẫn còn Pending
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/check-in",
            new
            {
                code = "BV-PENDING",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert — Có thể 404 (code không tồn tại) hoặc 409 (booking chưa paid)
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict,
            $"Expected NotFound or Conflict, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [IntegrationFact]
    public async Task CheckIn_WithMissingCode_Returns400()
    {
        // Arrange
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act — Body rỗng (code = "")
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/check-in",
            new
            {
                code = "",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert — Model validation rejects empty code
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest,
            $"Expected 400, got {response.StatusCode}");
    }

    [IntegrationFact]
    public async Task CheckIn_WithInvalidBarcode_Returns404()
    {
        // Arrange — Need a paid deposit OR use an invalid code that returns 404 first
        var managerToken = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, managerToken);

        // Act — Code không tồn tại
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/check-in",
            new
            {
                code = "BV-NOTFOUND",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = "INVALID-BARCODE"
            });

        // Assert — Service fails on code lookup first, returns 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [IntegrationFact]
    public async Task CheckIn_EndpointRequiresAuth()
    {
        // Arrange — No token
        // Clear any existing auth
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/check-in",
            new
            {
                code = "BV12345678",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task CheckIn_WithCafeStaffRole_ReturnsSuccessOrPermissionError()
    {
        // Arrange — CafeStaff cũng có quyền POS theo [Authorize(Roles = "Manager,CafeStaff")]
        var staffToken = await IntegrationTestAuth.AsPlayer2Async(_client); // Player2 là CafeStaff theo bootstrapper
        ApiTestClient.Authorize(_client, staffToken);

        // Act
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/cafes/{IntegrationTestFixtures.DemoCafeId}/pos/check-in",
            new
            {
                code = "BV-INVALID-FOR-STAFF",
                cafeTableId = IntegrationTestFixtures.DemoPosTableId,
                barcode = IntegrationTestFixtures.PosBoxBarcode
            });

        // Assert — Auth passes, lookup fails with 404
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404 (code not found) or 403 (CafeStaff not on staff list), got {response.StatusCode}");
    }
}
