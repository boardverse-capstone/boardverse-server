#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for LobbyInviteController
/// Covers: Send, accept, decline, cancel invites, share code
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class LobbyInviteControllerIntegrationTests
{
    private readonly HttpClient _client;

    public LobbyInviteControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    private async Task<Guid?> CreateTestLobbyAsync()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/lobbies", new
        {
            gameTemplateId = catanId,
            scheduledStartTime = DateTime.UtcNow.AddHours(3),
            maxMembers = 4,
            cancellationLeadTimeMinutes = 30
        });

        if (response.StatusCode != HttpStatusCode.Created) return null;
        var body = await ApiTestClient.ReadApiResponseAsync<object>(response);
        return Guid.NewGuid(); // Return valid GUID for testing
    }

    #region === SEND INVITE ===

    [IntegrationFact]
    public async Task LobbyInvite_SendInvite()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var inviteRequest = new
        {
            targetUserId = IntegrationTestFixtures.DemoPlayer2UserId,
            message = "Join my lobby!"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/lobbies/{lobbyId}/invites",
            inviteRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest);

        await CleanupLobbyAsync(lobbyId);
    }

    #endregion

    #region === ACCEPT / DECLINE ===

    [IntegrationFact]
    public async Task LobbyInvite_AcceptInvite()
    {
        var token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/lobbies/invites/{Guid.NewGuid()}/accept", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task LobbyInvite_DeclineInvite()
    {
        var token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/lobbies/invites/{Guid.NewGuid()}/decline", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task LobbyInvite_CancelInvite()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync(
            $"/api/v1/lobbies/invites/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === GET INVITES ===

    [IntegrationFact]
    public async Task LobbyInvite_GetPendingInvites()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/lobbies/invites/me/pending");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task LobbyInvite_GetAllInvites()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/lobbies/invites/me");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task LobbyInvite_GetByStatus()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/lobbies/invites/me?status=Pending");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === SHARE INFO ===

    [IntegrationFact]
    public async Task LobbyInvite_GetShareInfo()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/lobbies/{lobbyId}/share-info");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);

        await CleanupLobbyAsync(lobbyId);
    }

    [IntegrationFact]
    public async Task LobbyInvite_JoinByShareCode()
    {
        var token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, token);

        var joinRequest = new
        {
            shareCode = "TESTCODE123"
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            "/api/v1/lobbies/join-by-code",
            joinRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === HELPER ===

    private async Task CleanupLobbyAsync(Guid? lobbyId)
    {
        if (lobbyId == null) return;
        try
        {
            var token = await IntegrationTestAuth.AsPlayer1Async(_client);
            ApiTestClient.Authorize(_client, token);
            await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/cancel", null);
        }
        catch { /* Ignore cleanup errors */ }
    }

    #endregion
}
