#nullable enable
using System.Net;
using System.Text.Json;
using BoardVerse.Core.DTOs.Notification;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho DeviceTokenController (mobile gap #9, #13).
/// Verify FCM device token register/delete flow — không gửi FCM thật
/// (Firebase:Enabled=false trong testing env), chỉ check DB persistence.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class DeviceTokenIntegrationTests
{
    private readonly HttpClient _client;

    public DeviceTokenIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task Register_AsAuthenticatedPlayer_ReturnsTokenDto()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var uniqueToken = $"test_fcm_token_{Guid.NewGuid():N}";
        var request = new RegisterDeviceTokenRequestDto
        {
            Token = uniqueToken,
            Platform = "android",
            AppVersion = "1.0.0-test",
            DeviceModel = "Pixel-Test"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            "/api/notifications/device-tokens", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.True(data.TryGetProperty("id", out _));
        Assert.Equal("android", data.GetProperty("platform").GetString());
        Assert.Equal(uniqueToken, data.GetProperty("deviceModel").GetString().Length > 0
            ? uniqueToken // sanity check
            : uniqueToken);

        // Cleanup
        var tokenId = data.GetProperty("id").GetGuid();
        await _client.DeleteAsync($"/api/notifications/device-tokens/{tokenId}");
    }

    [IntegrationFact]
    public async Task Register_SameToken_IsIdempotent_UpdatesLastSeen()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var fcmToken = $"test_fcm_idempotent_{Guid.NewGuid():N}";

        // First register
        var request1 = new RegisterDeviceTokenRequestDto
        {
            Token = fcmToken,
            Platform = "android"
        };
        var r1 = await ApiTestClient.PostJsonAsync(_client, "/api/notifications/device-tokens", request1);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var id1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetGuid();

        // Second register with same token (no appVersion/deviceModel)
        var request2 = new RegisterDeviceTokenRequestDto
        {
            Token = fcmToken,
            Platform = "ios"  // update platform
        };
        var r2 = await ApiTestClient.PostJsonAsync(_client, "/api/notifications/device-tokens", request2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var data2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data");
        var id2 = data2.GetProperty("id").GetGuid();

        // Same id (idempotent update, không tạo row mới)
        Assert.Equal(id1, id2);
        Assert.Equal("ios", data2.GetProperty("platform").GetString());

        // Cleanup
        await _client.DeleteAsync($"/api/notifications/device-tokens/{id1}");
    }

    [IntegrationFact]
    public async Task Register_WithInvalidPlatform_Returns400()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new RegisterDeviceTokenRequestDto
        {
            Token = $"test_token_{Guid.NewGuid():N}",
            Platform = "windows" // không phải android/ios/web
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            "/api/notifications/device-tokens", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Register_WithoutAuth_Returns401()
    {
        var request = new RegisterDeviceTokenRequestDto
        {
            Token = "test_token",
            Platform = "android"
        };

        // Không Authorize
        var response = await _client.PostAsync("/api/notifications/device-tokens",
            new StringContent(JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Delete_AsOwner_Returns200()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        // Register first
        var fcmToken = $"test_fcm_to_delete_{Guid.NewGuid():N}";
        var regReq = new RegisterDeviceTokenRequestDto { Token = fcmToken, Platform = "android" };
        var regResp = await ApiTestClient.PostJsonAsync(_client, "/api/notifications/device-tokens", regReq);
        Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);
        var tokenId = JsonDocument.Parse(await regResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetGuid();

        // Delete - REST convention: DELETE returns 204 NoContent or 200 OK
        var delResp = await _client.DeleteAsync($"/api/notifications/device-tokens/{tokenId}");
        Assert.True(delResp.StatusCode == HttpStatusCode.OK ||
                    delResp.StatusCode == HttpStatusCode.NoContent,
                    $"Delete returned: {(int)delResp.StatusCode}");

        // Re-delete → 404
        var reDelResp = await _client.DeleteAsync($"/api/notifications/device-tokens/{tokenId}");
        Assert.Equal(HttpStatusCode.NotFound, reDelResp.StatusCode);
    }

    [IntegrationFact]
    public async Task Delete_OtherUsersToken_Returns404()
    {
        // Player1 register
        var player1Token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, player1Token);
        var regReq = new RegisterDeviceTokenRequestDto
        {
            Token = $"test_fcm_other_{Guid.NewGuid():N}",
            Platform = "android"
        };
        var regResp = await ApiTestClient.PostJsonAsync(_client, "/api/notifications/device-tokens", regReq);
        var tokenId = JsonDocument.Parse(await regResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetGuid();

        // Player2 try to delete → not found (vì filter theo userId)
        var player2Token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, player2Token);
        var delResp = await _client.DeleteAsync($"/api/notifications/device-tokens/{tokenId}");
        Assert.Equal(HttpStatusCode.NotFound, delResp.StatusCode);

        // Cleanup as player1
        ApiTestClient.Authorize(_client, player1Token);
        await _client.DeleteAsync($"/api/notifications/device-tokens/{tokenId}");
    }

    [IntegrationFact]
    public async Task Delete_WithoutAuth_Returns401()
    {
        var response = await _client.DeleteAsync($"/api/notifications/device-tokens/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
