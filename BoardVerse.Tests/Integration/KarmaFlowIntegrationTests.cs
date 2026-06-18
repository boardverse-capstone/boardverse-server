using System.Net;
using BoardVerse.Core.Data;
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
            DevSeedConstants.Player1Email,
            DevSeedConstants.DemoPlayerPassword);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/users/ratings/karma/lobbies/{DevSeedConstants.DemoKarmaLobbyId}");

        response.EnsureSuccessStatusCode();
        var body = await ApiTestClient.ReadApiResponseAsync<KarmaContextDto>(response);
        Assert.Equal(DevSeedConstants.DemoKarmaLobbyId, body.Data!.LobbyId);
        Assert.NotEmpty(body.Data.MembersToRate);
    }

    [IntegrationFact]
    public async Task OpenKarmaWindow_ThenSubmitRating_AsDevUsers()
    {
        var adminToken = await ApiTestClient.LoginAsync(
            _client,
            DevSeedConstants.AdminEmail,
            DevSeedConstants.AdminPassword);
        ApiTestClient.Authorize(_client, adminToken);

        var openResponse = await _client.PostAsync(
            $"/api/v1/lobbies/{DevSeedConstants.DemoKarmaLobbyId}/karma-rating/open",
            null);

        Assert.True(
            openResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            await openResponse.Content.ReadAsStringAsync());

        var playerToken = await ApiTestClient.LoginAsync(
            _client,
            DevSeedConstants.Player1Email,
            DevSeedConstants.DemoPlayerPassword);
        ApiTestClient.Authorize(_client, playerToken);

        var submitResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/users/ratings/karma", new
        {
            lobbyId = DevSeedConstants.DemoKarmaLobbyId,
            ratings = new[]
            {
                new
                {
                    targetUserId = DevSeedConstants.DemoPlayer2UserId,
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
