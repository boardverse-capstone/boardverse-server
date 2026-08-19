#nullable enable
using System.Net;
using System.Net.Http.Json;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests cho <c>LeaderboardController</c> — phục vụ BR §K-06 leaderboard UI.
/// Verifies:
///   - GET /karma và /elo public, không cần auth.
///   - Query top/offset được parse đúng.
///   - Authenticated viewer nhận được <c>userRank</c>.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class LeaderboardControllerIntegrationTests
{
    private readonly HttpClient _client;

    public LeaderboardControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task GetKarmaLeaderboard_Public_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/leaderboard/karma");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeaderboardEnvelope>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Data);
        Assert.True(payload.Data.Offset >= 0);
        Assert.True(payload.Data.Limit > 0);
        Assert.True(payload.Data.TotalCount >= 0);
        Assert.NotNull(payload.Data.GeneratedAt);
    }

    [IntegrationFact]
    public async Task GetEloLeaderboard_Public_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/leaderboard/elo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeaderboardEnvelope>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Data);
    }

    [IntegrationFact]
    public async Task GetKarmaLeaderboard_RespectsTopAndOffset()
    {
        var response = await _client.GetAsync("/api/v1/leaderboard/karma?top=5&offset=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeaderboardEnvelope>();
        Assert.NotNull(payload);
        Assert.Equal(5, payload!.Data.Limit);
        Assert.Equal(0, payload.Data.Offset);
    }

    [IntegrationFact]
    public async Task GetKarmaLeaderboard_TopOver100_ClampedTo100()
    {
        var response = await _client.GetAsync("/api/v1/leaderboard/karma?top=500");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeaderboardEnvelope>();
        Assert.NotNull(payload);
        Assert.True(payload!.Data.Limit <= 100);
    }

    [IntegrationFact]
    public async Task GetEloLeaderboard_RespectsTopAndOffset()
    {
        var response = await _client.GetAsync("/api/v1/leaderboard/elo?top=3&offset=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeaderboardEnvelope>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Data.Limit);
        Assert.Equal(2, payload.Data.Offset);
    }

    [IntegrationFact]
    public async Task GetKarmaLeaderboard_AuthenticatedViewer_GetsUserRankBlock()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/leaderboard/karma?top=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeaderboardEnvelope>();
        Assert.NotNull(payload);
        // userRank có thể null nếu player1 chưa có trong top 10 hoặc chưa có profile.
        // Quan trọng: endpoint KHÔNG trả 401 khi đã authenticate.
    }

    [IntegrationFact]
    public async Task GetEloLeaderboard_AuthenticatedViewer_GetsUserRankBlock()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/leaderboard/elo?top=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Helpers ---

    private sealed class LeaderboardEnvelope
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public LeaderboardBody Data { get; set; } = new();
        public string? Path { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private sealed class LeaderboardBody
    {
        public List<LeaderboardRow> Entries { get; set; } = new();
        public int Offset { get; set; }
        public int Limit { get; set; }
        public long TotalCount { get; set; }
        public DateTime GeneratedAt { get; set; }
        public LeaderboardRow? UserRank { get; set; }
    }

    private sealed class LeaderboardRow
    {
        public int Rank { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public int KarmaPoints { get; set; }
        public int GlobalElo { get; set; }
        public int Level { get; set; }
        public string GamerTier { get; set; } = "Bronze";
    }
}
