using System.Net;
using BoardVerse.Core.Data;
using BoardVerse.Core.Enum;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class MatchFlowIntegrationTests
{
    private readonly HttpClient _client;

    public MatchFlowIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [IntegrationFact]
    public async Task MatchResultStatus_DevLobby_ReturnsForMember()
    {
        var token = await ApiTestClient.LoginAsync(
            _client,
            DevSeedConstants.Player1Email,
            DevSeedConstants.DemoPlayerPassword);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/matches/results/lobbies/{DevSeedConstants.DemoMatchLobbyId}");

        response.EnsureSuccessStatusCode();
        var body = await ApiTestClient.ReadApiResponseAsync<MatchStatusDto>(response);
        Assert.Equal(DevSeedConstants.DemoMatchLobbyId, body.Data!.LobbyId);
        Assert.True(body.Data.SupportsMatchResults);
    }

    [IntegrationFact]
    public async Task SubmitMatchResult_WhenNotFinalized_RecordsSubmissionOrFinalizes()
    {
        var player1Token = await ApiTestClient.LoginAsync(
            _client,
            DevSeedConstants.Player1Email,
            DevSeedConstants.DemoPlayerPassword);
        ApiTestClient.Authorize(_client, player1Token);

        var statusResponse = await _client.GetAsync(
            $"/api/v1/matches/results/lobbies/{DevSeedConstants.DemoMatchLobbyId}");
        statusResponse.EnsureSuccessStatusCode();
        var status = (await ApiTestClient.ReadApiResponseAsync<MatchStatusDto>(statusResponse)).Data!;

        if (status.ConsensusStatus is "Finalized" or "Conflict")
        {
            Assert.True(status.SubmittedCount >= 1);
        }
        else
        {
            var submitResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/matches/results", new
            {
                lobbyId = DevSeedConstants.DemoMatchLobbyId,
                outcome = MatchOutcome.Win
            });

            Assert.True(
                submitResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                await submitResponse.Content.ReadAsStringAsync());
        }
    }

    private sealed class MatchStatusDto
    {
        public Guid LobbyId { get; set; }
        public bool SupportsMatchResults { get; set; }
        public string ConsensusStatus { get; set; } = string.Empty;
        public int SubmittedCount { get; set; }
    }
}
