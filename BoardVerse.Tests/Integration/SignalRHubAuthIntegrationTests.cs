#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Regression tests cho SignalR hub authentication.
///
/// Background: Hub endpoints (<c>/hubs/lobby</c>, <c>/hubs/pos</c>) được khai báo với
/// <c>[Authorize]</c>, nhưng browser/mobile SignalR client không thể set custom header trên
/// WebSocket handshake — phải truyền JWT qua query string <c>?access_token=...</c>.
/// JwtBearer mặc định chỉ đọc <c>Authorization</c> header, nên trước khi fix tất cả
/// negotiate request đều fail với 401 + <c>AuthorizationHeaderMissing</c>.
///
/// Fix: thêm <c>OnMessageReceived</c> trong <c>Program.cs</c> để lift <c>access_token</c>
/// query string lên bearer pipeline cho path <c>/hubs/*</c> mà thôi.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SignalRHubAuthIntegrationTests
{
    private readonly HttpClient _client;

    public SignalRHubAuthIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task PosHub_Negotiate_WithAccessTokenQuery_ReturnsSuccess()
    {
        // Arrange — login để có token hợp lệ.
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.ClearAuth(_client); // bỏ header mặc định để mô phỏng WebSocket client thuần.

        // Act — gọi negotiate với access_token trên query string (đúng cách SignalR client làm).
        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/pos/negotiate?negotiateVersion=1")
        {
            Headers = { },
        };
        // Query string phải chứa access_token đúng tên (SignalR convention).
        request.RequestUri = new Uri(
            $"/hubs/pos/negotiate?access_token={Uri.EscapeDataString(token)}",
            UriKind.Relative);

        var response = await _client.SendAsync(request);

        // Assert — trước fix: 401 AuthorizationHeaderMissing; sau fix: 200.
        Assert.True(
            response.IsSuccessStatusCode,
            $"PosHub negotiate with ?access_token should succeed. Got {(int)response.StatusCode} {response.StatusCode}.");
    }

    [IntegrationFact]
    public async Task LobbyHub_Negotiate_WithAccessTokenQuery_ReturnsSuccess()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.ClearAuth(_client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/lobby/negotiate")
        {
            RequestUri = new Uri(
                $"/hubs/lobby/negotiate?access_token={Uri.EscapeDataString(token)}",
                UriKind.Relative),
        };

        var response = await _client.SendAsync(request);

        Assert.True(
            response.IsSuccessStatusCode,
            $"LobbyHub negotiate with ?access_token should succeed. Got {(int)response.StatusCode} {response.StatusCode}.");
    }

    [IntegrationFact]
    public async Task PosHub_Negotiate_WithoutAnyToken_ReturnsUnauthorized()
    {
        // Không có header, không có query — phải fail (giữ nguyên hành vi bảo mật).
        ApiTestClient.ClearAuth(_client);

        var response = await _client.PostAsync("/hubs/pos/negotiate", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task PosHub_Negotiate_WithMalformedAccessToken_ReturnsUnauthorized()
    {
        // access_token là chuỗi rác (không phải JWT 3 phần) — handler phải từ chối.
        ApiTestClient.ClearAuth(_client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/pos/negotiate")
        {
            RequestUri = new Uri("/hubs/pos/negotiate?access_token=not-a-jwt", UriKind.Relative),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}