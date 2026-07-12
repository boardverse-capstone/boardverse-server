using System.Net;
using BoardVerse.Core.Enum;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class KarmaFlowIntegrationTests
{
    private readonly HttpClient _client;

    public KarmaFlowIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task KarmaRatingContext_DevLobby_ReturnsMembers()
    {
        var token = await ApiTestClient.LoginAsync(
            _client,
            IntegrationTestFixtures.Player1Email,
            IntegrationTestFixtures.PlayerPassword);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/users/ratings/karma/lobbies/{IntegrationTestFixtures.DemoKarmaLobbyId}");

        response.EnsureSuccessStatusCode();
        var body = await ApiTestClient.ReadApiResponseAsync<KarmaContextDto>(response);
        Assert.Equal(IntegrationTestFixtures.DemoKarmaLobbyId, body.Data!.LobbyId);
        Assert.NotEmpty(body.Data.MembersToRate);
    }

    [IntegrationFact]
    public async Task OpenKarmaWindow_ThenSubmitRating_AsDevUsers()
    {
        // BR-09: Chỉ Host mới có thể mở cửa sổ đánh giá Karma
        var player1Token = await ApiTestClient.LoginAsync(
            _client,
            IntegrationTestFixtures.Player1Email,
            IntegrationTestFixtures.PlayerPassword);
        ApiTestClient.Authorize(_client, player1Token);

        var openResponse = await _client.PostAsync(
            $"/api/v1/lobbies/{IntegrationTestFixtures.DemoKarmaLobbyId}/open-karma-window",
            null);

        Assert.True(
            openResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            await openResponse.Content.ReadAsStringAsync());

        var player2Token = await ApiTestClient.LoginAsync(
            _client,
            IntegrationTestFixtures.Player2Email,
            IntegrationTestFixtures.PlayerPassword);
        ApiTestClient.Authorize(_client, player2Token);

        // Player1 đánh giá Player2
        ApiTestClient.Authorize(_client, player1Token);
        var submitResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/users/ratings/karma", new
        {
            lobbyId = IntegrationTestFixtures.DemoKarmaLobbyId,
            ratings = new[]
            {
                new
                {
                    targetUserId = IntegrationTestFixtures.DemoPlayer2UserId,
                    tags = new[] { KarmaRatingTag.Friendly, KarmaRatingTag.OnTime }
                }
            }
        });

        Assert.True(
            submitResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            await submitResponse.Content.ReadAsStringAsync());
    }

    private sealed class KarmaContextDto
    {
        public Guid LobbyId { get; set; }
        public string LobbyStatus { get; set; } = string.Empty;
        public bool CanSubmitRatings { get; set; }
        public List<MemberTargetDto> MembersToRate { get; set; } = [];
    }

    private sealed class MemberTargetDto
    {
        public Guid UserId { get; set; }
        public bool AlreadyRated { get; set; }
    }
}
