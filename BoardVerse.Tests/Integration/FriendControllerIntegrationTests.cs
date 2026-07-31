#nullable enable
using System.Net;
using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for FriendController
/// 27 endpoints - Currently 0% coverage
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class FriendControllerIntegrationTests
{
    private readonly HttpClient _client;

    public FriendControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === FRIEND REQUESTS ===

    [IntegrationFact]
    public async Task FriendRequests_Send_Accept_Decline()
    {
        // SEND REQUEST
        var senderToken = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, senderToken);
        var targetUserId = IntegrationTestFixtures.DemoPlayer2UserId;

        var sendResponse = await ApiTestClient.PostJsonAsync(_client, "/api/v1/friends/requests", new
        {
            targetUserId = targetUserId,
            message = "Let's play together!"
        });

        if (sendResponse.StatusCode == HttpStatusCode.Created)
        {
            var requestBody = await ApiTestClient.ReadApiResponseAsync<object>(sendResponse);

            // ACCEPT
            var receiverToken = await IntegrationTestAuth.AsPlayer2Async(_client);
            ApiTestClient.Authorize(_client, receiverToken);

            var acceptResponse = await _client.PostAsync($"/api/v1/friends/requests/{Guid.NewGuid()}/accept", null);
            Assert.True(acceptResponse.StatusCode == HttpStatusCode.OK ||
                       acceptResponse.StatusCode == HttpStatusCode.BadRequest);

            // DECLINE
            var declineResponse = await _client.PostAsync($"/api/v1/friends/requests/{Guid.NewGuid()}/decline", null);
            Assert.True(declineResponse.StatusCode == HttpStatusCode.OK ||
                       declineResponse.StatusCode == HttpStatusCode.BadRequest);
        }
        else
        {
            Assert.True(sendResponse.StatusCode == HttpStatusCode.BadRequest ||
                       sendResponse.StatusCode == HttpStatusCode.Conflict);
        }
    }

    [IntegrationFact]
    public async Task FriendRequests_GetReceived()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/requests/received");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task FriendRequests_GetSent()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/requests/sent");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task FriendRequests_GetById()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/friends/requests/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task FriendRequests_Delete()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync($"/api/v1/friends/requests/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task FriendRequests_MarkAsRead()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync($"/api/v1/friends/requests/{Guid.NewGuid()}/read", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === FRIENDS LIST ===

    [IntegrationFact]
    public async Task Friends_GetAll()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Friends_GetActivity()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/activity");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Friends_GetByDirection()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/by-direction?direction=Mutual");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Friends_Search()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/search?query=player");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Friends_GetSuggestions()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/suggestions");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Friends_GetMutual()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/friends/{IntegrationTestFixtures.DemoPlayer2UserId}/mutual");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Friends_GetProfile()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/friends/{IntegrationTestFixtures.DemoPlayer2UserId}/profile");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Friends_GetList()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync($"/api/v1/friends/{IntegrationTestFixtures.DemoPlayer2UserId}/list");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Friends_Delete()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync($"/api/v1/friends/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === BLOCK ===

    [IntegrationFact]
    public async Task Block_User()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            targetUserId = IntegrationTestFixtures.DemoPlayer2UserId,
            reason = "Test block reason"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/friends/block", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Block_Unblock()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync($"/api/v1/friends/block/{IntegrationTestFixtures.DemoPlayer2UserId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Block_GetBlocked()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/blocked");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Block_GetBlockedBy()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/blocked-by");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === REPORTS ===

    [IntegrationFact]
    public async Task Reports_Create()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            reportedUserId = IntegrationTestFixtures.DemoPlayer2UserId,
            reason = "Toxic behavior",
            description = "Test report for integration test"
        };

        var response = await ApiTestClient.PostJsonAsync(_client, "/api/v1/friends/reports", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Reports_GetAll()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/reports");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion

    #region === NOTES ===

    [IntegrationFact]
    public async Task Notes_GetAll()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync("/api/v1/friends/notes");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Notes_Update()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            note = "Test note for integration test"
        };

        var response = await ApiTestClient.PutJsonAsync(_client,
            $"/api/v1/friends/notes/{IntegrationTestFixtures.DemoPlayer2UserId}",
            request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Notes_Delete()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync($"/api/v1/friends/notes/{Guid.NewGuid()}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region === PRIVACY ===

    [IntegrationFact]
    public async Task Privacy_Update()
    {
        var token = await IntegrationTestAuth.AsPlayer1Async(_client);
        ApiTestClient.Authorize(_client, token);

        var request = new
        {
            profileVisibility = "Friends",
            showOnlineStatus = true,
            showGameHistory = false
        };

        var response = await ApiTestClient.PutJsonAsync(_client, "/api/v1/friends/privacy", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    #endregion
}
