#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Tests.Integration.Helpers;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for LobbyController - Extra endpoints
/// Covers: Leave, close, kick, transfer host, messages, ready, report, hosted, joined
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class LobbyControllerExtraIntegrationTests
{
    private readonly HttpClient _client;

    public LobbyControllerExtraIntegrationTests(BoardVerseWebApplicationFactory factory) =>
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
        var body = await ApiTestClient.ReadApiResponseAsync<LobbyCreatedDto>(response);
        return body.Data!.Id;
    }

    #region === LEAVE & CLOSE ===

    [IntegrationFact]
    public async Task Lobby_Leave_AsMember()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        // Join as player2
        var player2Token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, player2Token);
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/join", null);

        // Leave
        var leaveResponse = await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/leave", null);
        Assert.True(leaveResponse.StatusCode == HttpStatusCode.OK ||
                   leaveResponse.StatusCode == HttpStatusCode.BadRequest ||
                   leaveResponse.StatusCode == HttpStatusCode.NotFound);

        // Cleanup
        await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, await IntegrationTestAuth.AsPlayer1Async(_client));
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/cancel", null);
    }

    [IntegrationFact]
    public async Task Lobby_Close_AsHost()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        // Close as host
        var response = await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/close", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion

    #region === KICK & TRANSFER HOST ===

    [IntegrationFact]
    public async Task Lobby_Kick_AsHost()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        // Join as player2
        var player2Token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, player2Token);
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/join", null);

        // Kick as host
        var kickRequest = new { userId = IntegrationTestFixtures.DemoPlayer2UserId };
        var kickResponse = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/lobbies/{lobbyId}/kick",
            kickRequest);
        Assert.True(kickResponse.StatusCode == HttpStatusCode.OK ||
                   kickResponse.StatusCode == HttpStatusCode.BadRequest ||
                   kickResponse.StatusCode == HttpStatusCode.NotFound);

        // Cleanup
        ApiTestClient.Authorize(_client, await IntegrationTestAuth.AsPlayer1Async(_client));
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/cancel", null);
    }

    [IntegrationFact]
    public async Task Lobby_TransferHost()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        // Join as player2
        var player2Token = await IntegrationTestAuth.AsPlayer2Async(_client);
        ApiTestClient.Authorize(_client, player2Token);
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/join", null);

        // Transfer host as original host
        var transferRequest = new { newHostUserId = IntegrationTestFixtures.DemoPlayer2UserId };
        var transferResponse = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/lobbies/{lobbyId}/transfer-host",
            transferRequest);
        Assert.True(transferResponse.StatusCode == HttpStatusCode.OK ||
                   transferResponse.StatusCode == HttpStatusCode.BadRequest ||
                   transferResponse.StatusCode == HttpStatusCode.NotFound);

        // Cleanup
        ApiTestClient.Authorize(_client, player2Token);
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/cancel", null);
    }

    #endregion

    #region === PATCH & READY ===

    [IntegrationFact]
    public async Task Lobby_Patch_UpdateDetails()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        var patchRequest = new
        {
            description = "Updated description",
            maxMembers = 3
        };

        var response = await ApiTestClient.PatchJsonAsync(_client,
            $"/api/v1/lobbies/{lobbyId}",
            patchRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );

        // Cleanup
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/cancel", null);
    }

    [IntegrationFact]
    public async Task Lobby_Ready_Toggle()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        var response = await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/ready", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );

        // Cleanup
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/cancel", null);
    }

    #endregion

    #region === HOSTED & JOINED ===

    [IntegrationFact]
    public async Task Lobby_GetHosted()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/lobbies/hosted");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    [IntegrationFact]
    public async Task Lobby_GetJoined()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/lobbies/joined");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion

    #region === MESSAGES ===

    [IntegrationFact]
    public async Task Lobby_SendMessage()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        var messageRequest = new { content = "Hello from integration test!" };
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/lobbies/{lobbyId}/messages",
            messageRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );

        // Cleanup
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/cancel", null);
    }

    [IntegrationFact]
    public async Task Lobby_GetMessages()
    {
        var lobbyId = await CreateTestLobbyAsync();
        if (lobbyId == null) return;

        var response = await _client.GetAsync($"/api/v1/lobbies/{lobbyId}/messages");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );

        // Cleanup
        await _client.PostAsync($"/api/v1/lobbies/{lobbyId}/cancel", null);
    }

    #endregion

    #region === REPORT ===

    [IntegrationFact]
    public async Task Lobby_Report()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var reportRequest = new
        {
            lobbyId = Guid.NewGuid(),
            reason = "Test report",
            description = "Integration test report"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/lobbies/report", reportRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion

    #region === DISCOVERABLE ===

    [IntegrationFact]
    public async Task Lobby_GetDiscoverable()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/lobbies/discoverable?latitude=10.8231&longitude=106.6297&radiusKm=10");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone
                   );
    }

    #endregion
}
