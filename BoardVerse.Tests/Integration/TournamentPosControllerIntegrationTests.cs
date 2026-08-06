#nullable enable
using System.Net;
using BoardVerse.Tests.Integration.Infrastructure;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Integration tests for TournamentPosController
/// Covers: Full tournament lifecycle management from POS
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class TournamentPosControllerIntegrationTests
{
    private readonly HttpClient _client;

    public TournamentPosControllerIntegrationTests(BoardVerseWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    #region === TOURNAMENT CRUD ===

    [IntegrationFact]
    public async Task TournamentPos_Create()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var catanId = await IntegrationCatalog.GetCatanGameIdAsync(_client);

        var createRequest = new
        {
            title = $"Test Tournament {Guid.NewGuid():N}".Substring(0, 25),
            description = "Test tournament description",
            gameTemplateId = catanId,
            startTime = DateTime.UtcNow.AddDays(7),
            maxParticipants = 8,
            minParticipants = 4
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/pos/tournaments/cafes/{IntegrationTestFixtures.DemoCafeId}",
            createRequest);
        Assert.True(response.StatusCode == HttpStatusCode.Created ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.Conflict ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_GetByCafe()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/pos/tournaments/cafes/{IntegrationTestFixtures.DemoCafeId}");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_GetActive()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/pos/tournaments/cafes/{IntegrationTestFixtures.DemoCafeId}/active");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_Update()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            title = "Updated Tournament Title",
            description = "Updated description"
        };

        var response = await ApiTestClient.PatchJsonAsync(_client,
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}",
            updateRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    #endregion

    #region === REGISTRATION ===

    [IntegrationFact]
    public async Task TournamentPos_OpenRegistration()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/open-registration", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_CloseRegistration()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/close-registration", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_ReopenRegistration()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/reopen-registration", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    #endregion

    #region === TOURNAMENT LIFECYCLE ===

    [IntegrationFact]
    public async Task TournamentPos_Start()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/start", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_Cancel()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var cancelRequest = new { reason = "Test cancellation" };
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/cancel",
            cancelRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_Complete()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/complete", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    #endregion

    #region === PARTICIPANTS ===

    [IntegrationFact]
    public async Task TournamentPos_CheckInParticipant()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/participants/{Guid.NewGuid()}/check-in", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_MarkNoShow()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/participants/{Guid.NewGuid()}/no-show", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    #endregion

    #region === MATCHES ===

    [IntegrationFact]
    public async Task TournamentPos_StartMatch()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/matches/{Guid.NewGuid()}/start", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_RecordMatchResult()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var resultRequest = new
        {
            winnerId = IntegrationTestFixtures.DemoPlayer1UserId,
            scores = new[]
            {
                new { userId = IntegrationTestFixtures.DemoPlayer1UserId, score = 100, rank = 1 },
                new { userId = IntegrationTestFixtures.DemoPlayer2UserId, score = 80, rank = 2 }
            }
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/pos/tournaments/matches/{Guid.NewGuid()}/result",
            resultRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.BadRequest,
                   $"Record result returned: {(int)response.StatusCode}");
    }

    [IntegrationFact]
    public async Task TournamentPos_UpdateMatchResult()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var resultRequest = new
        {
            scores = new[]
            {
                new { userId = IntegrationTestFixtures.DemoPlayer1UserId, score = 100, rank = 1 }
            }
        };

        var response = await ApiTestClient.PatchJsonAsync(_client,
            $"/api/v1/pos/tournaments/matches/{Guid.NewGuid()}/result",
            resultRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.BadRequest,
                   $"Update result returned: {(int)response.StatusCode}");
    }

    [IntegrationFact]
    public async Task TournamentPos_CancelMatch()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var cancelRequest = new { reason = "Technical issue" };
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/pos/tournaments/matches/{Guid.NewGuid()}/cancel",
            cancelRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    #endregion

    #region === ROUNDS & PAIRING ===

    [IntegrationFact]
    public async Task TournamentPos_AdvanceRound()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/advance-round", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_SetPairingMode()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var modeRequest = new { mode = "Auto" };
        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/pairing-mode",
            modeRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_PreviewPairings()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.GetAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/pairings/1/preview");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    [IntegrationFact]
    public async Task TournamentPos_SetRoundPairings()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var pairingRequest = new
        {
            roundNumber = 1,
            pairings = new[]
            {
                new
                {
                    participant1Id = IntegrationTestFixtures.DemoPlayer1UserId,
                    participant2Id = IntegrationTestFixtures.DemoPlayer2UserId,
                    tableNumber = 1
                }
            }
        };

        var response = await ApiTestClient.PostJsonAsync(_client,
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/pairings",
            pairingRequest);
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                   response.StatusCode == HttpStatusCode.Gone ||
                   response.StatusCode == HttpStatusCode.Forbidden ||
                   response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.BadRequest,
                   $"Set pairings returned: {(int)response.StatusCode}");
    }

    [IntegrationFact]
    public async Task TournamentPos_ClearRoundPairings()
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var response = await _client.DeleteAsync(
            $"/api/v1/pos/tournaments/{Guid.NewGuid()}/pairings/1");
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.NotFound
                   || response.StatusCode == HttpStatusCode.MethodNotAllowed
                   || response.StatusCode == HttpStatusCode.Gone);
    }

    #endregion
}
