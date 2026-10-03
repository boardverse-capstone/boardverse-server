#nullable enable
using System.Net;
using System.Net.Http.Json;
using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Tests.Integration;

/// <summary>
/// Happy case integration tests cho 3 endpoint Tournament POS:
///   - PATCH /api/v1/pos/tournaments/{tournamentId} (Update — chỉ Draft)
///   - POST  /api/v1/pos/tournaments/{tournamentId}/open-registration (Draft → RegistrationOpen)
///   - GET   /api/v1/pos/tournaments/cafes/{cafeId}/active (list OnGoing, sort CurrentRound desc, StartTime asc)
/// Mỗi test self-contained: tạo tournament mới, assert response, không phụ thuộc thứ tự test khác.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class TournamentPosHappyCaseIntegrationTests
{
    private readonly HttpClient _client;
    private readonly BoardVerseWebApplicationFactory _factory;

    public TournamentPosHappyCaseIntegrationTests(BoardVerseWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ====================================================================
    // HELPERS
    // ====================================================================

    /// <summary>
    /// Tạo 1 tournament Draft cho demo cafe qua API. Trả về TournamentId.
    /// Validate StartTime = now+7d, RegistrationDeadline = now+6d, MaxParticipants=8.
    /// </summary>
    private async Task<Guid> CreateDraftTournamentAsync(string titleTag)
    {
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var title = $"Phase2 {titleTag}".Substring(0, Math.Min(30, $"Phase2 {titleTag}".Length));
        var createRequest = new
        {
            title,
            description = "Phase 2 happy case test tournament",
            gameTemplateId = IntegrationTestFixtures.SplendorGameTemplateId,
            startTime = DateTime.UtcNow.AddDays(7),
            registrationDeadline = DateTime.UtcNow.AddDays(6),
            maxParticipants = 8,
            minParticipants = 4
        };

        var response = await ApiTestClient.PostJsonAsync(
            _client,
            $"/api/v1/pos/tournaments/cafes/{IntegrationTestFixtures.DemoCafeId}",
            createRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<TournamentResponseDto>(response);
        Assert.NotNull(body.Data);
        Assert.Equal(TournamentStatus.Draft, body.Data!.Status);
        return body.Data!.Id;
    }

    // ====================================================================
    // PATCH /api/v1/pos/tournaments/{tournamentId}
    // ====================================================================

    [IntegrationFact]
    public async Task UpdateTournament_HappyCase_UpdatesTitleAndDescription()
    {
        // Arrange
        var tournamentId = await CreateDraftTournamentAsync($"Upd-{Guid.NewGuid():N}".Substring(0, 12));
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            title = "Updated Title Phase2 Tournament",
            description = "Updated description for happy case test"
        };

        // Act
        var response = await ApiTestClient.PatchJsonAsync(
            _client,
            $"/api/v1/pos/tournaments/{tournamentId}",
            updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<TournamentResponseDto>(response);
        Assert.NotNull(body.Data);
        Assert.Equal(tournamentId, body.Data!.Id);
        Assert.Equal("Updated Title Phase2 Tournament", body.Data!.Title);
        Assert.Equal("Updated description for happy case test", body.Data!.Description);
        Assert.Equal(TournamentStatus.Draft, body.Data!.Status);  // PATCH không đổi status
    }

    [IntegrationFact]
    public async Task UpdateTournament_HappyCase_UpdatesConfiguration()
    {
        // Arrange
        var tournamentId = await CreateDraftTournamentAsync($"Cfg-{Guid.NewGuid():N}".Substring(0, 12));
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        var updateRequest = new
        {
            roundDurationMinutes = 60,
            maxParticipants = 16,
            minKarmaRequirement = 25,
            entryFee = 50000m,
            prize = "500.000 VND + 1 board game"
        };

        // Act
        var response = await ApiTestClient.PatchJsonAsync(
            _client,
            $"/api/v1/pos/tournaments/{tournamentId}",
            updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<TournamentResponseDto>(response);
        Assert.NotNull(body.Data);
        Assert.Equal(60, body.Data!.RoundDurationMinutes);
        Assert.Equal(16, body.Data!.MaxParticipants);
        Assert.Equal(25, body.Data!.MinKarmaRequirement);
        Assert.Equal(50000m, body.Data!.EntryFee);
        Assert.Equal("500.000 VND + 1 board game", body.Data!.Prize);
    }

    // ====================================================================
    // POST /api/v1/pos/tournaments/{tournamentId}/open-registration
    // ====================================================================

    [IntegrationFact]
    public async Task OpenRegistration_HappyCase_TransitionsFromDraftToRegistrationOpen()
    {
        // Arrange
        var tournamentId = await CreateDraftTournamentAsync($"Opn-{Guid.NewGuid():N}".Substring(0, 12));
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/pos/tournaments/{tournamentId}/open-registration",
            content: null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<TournamentResponseDto>(response);
        Assert.NotNull(body.Data);
        Assert.Equal(tournamentId, body.Data!.Id);
        Assert.Equal(TournamentStatus.RegistrationOpen, body.Data!.Status);
    }

    // ====================================================================
    // GET /api/v1/pos/tournaments/cafes/{cafeId}/active
    // ====================================================================

    [IntegrationFact]
    public async Task GetActiveTournaments_HappyCase_ReturnsOk()
    {
        // Arrange
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/pos/tournaments/cafes/{IntegrationTestFixtures.DemoCafeId}/active");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<List<TournamentResponseDto>>(response);
        Assert.NotNull(body.Data);
        // Có thể rỗng (không có tournament nào đang OnGoing) — chỉ verify call thành công.
    }

    [IntegrationFact]
    public async Task GetActiveTournaments_HappyCase_SortsByCurrentRoundDescThenStartTimeAsc()
    {
        // Arrange: cleanup tournaments cũ của demo cafe để có baseline sạch.
        await CleanupDemoCafeTournamentsAsync();

        // Arrange: tạo 3 tournament với StartTime khác nhau để test tiebreak.
        // Sau đó ép DB về Status=OnGoing + CurrentRound khác nhau.
        var t1Id = await CreateDraftTournamentAsync($"Sort1-{Guid.NewGuid():N}".Substring(0, 12));
        var t2Id = await CreateDraftTournamentAsync($"Sort2-{Guid.NewGuid():N}".Substring(0, 12));
        var t3Id = await CreateDraftTournamentAsync($"Sort3-{Guid.NewGuid():N}".Substring(0, 12));

        // Force OnGoing:
        //   t1: CurrentRound = 1, StartTime sớm nhất (5d)
        //   t2: CurrentRound = 2, StartTime giữa (7d)
        //   t3: CurrentRound = 2, StartTime muộn nhất (9d)
        // Expected sort (CurrentRound desc, StartTime asc):
        //   [0] = t2  (round 2, sớm)
        //   [1] = t3  (round 2, muộn)
        //   [2] = t1  (round 1)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
            // T1: round 1, StartTime = now+5d
            await db.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Tournaments"" SET ""Status"" = 3, ""CurrentRound"" = 1,
                         ""StartTime"" = {1}, ""StartedAt"" = {1}
                  WHERE ""Id"" = {0}",
                t1Id, DateTime.UtcNow.AddDays(5));
            // T2: round 2, StartTime = now+7d
            await db.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Tournaments"" SET ""Status"" = 3, ""CurrentRound"" = 2,
                         ""StartTime"" = {1}, ""StartedAt"" = {1}
                  WHERE ""Id"" = {0}",
                t2Id, DateTime.UtcNow.AddDays(7));
            // T3: round 2, StartTime = now+9d
            await db.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Tournaments"" SET ""Status"" = 3, ""CurrentRound"" = 2,
                         ""StartTime"" = {1}, ""StartedAt"" = {1}
                  WHERE ""Id"" = {0}",
                t3Id, DateTime.UtcNow.AddDays(9));
        }

        // Act
        var token = await IntegrationTestAuth.AsManagerAsync(_client);
        ApiTestClient.Authorize(_client, token);
        var response = await _client.GetAsync(
            $"/api/v1/pos/tournaments/cafes/{IntegrationTestFixtures.DemoCafeId}/active");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.ReadApiResponseAsync<List<TournamentResponseDto>>(response);
        Assert.NotNull(body.Data);
        Assert.Equal(3, body.Data!.Count);
        // Sort: CurrentRound desc → tiebreak StartTime asc.
        Assert.Equal(t2Id, body.Data![0].Id);  // round 2, StartTime sớm nhất (7d)
        Assert.Equal(t3Id, body.Data![1].Id);  // round 2, StartTime muộn hơn (9d)
        Assert.Equal(t1Id, body.Data![2].Id);  // round 1
    }

    /// <summary>
    /// Xóa sạch tournament (kèm participants, matches) của demo cafe để test sort có baseline sạch.
    /// </summary>
    private async Task CleanupDemoCafeTournamentsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            @"DELETE FROM ""TournamentParticipants""
              WHERE ""TournamentId"" IN (SELECT ""Id"" FROM ""Tournaments"" WHERE ""CafeId"" = {0})",
            IntegrationTestFixtures.DemoCafeId);
        await db.Database.ExecuteSqlRawAsync(
            @"DELETE FROM ""TournamentMatchBrackets""
              WHERE ""TournamentId"" IN (SELECT ""Id"" FROM ""Tournaments"" WHERE ""CafeId"" = {0})",
            IntegrationTestFixtures.DemoCafeId);
        await db.Database.ExecuteSqlRawAsync(
            @"DELETE FROM ""Tournaments"" WHERE ""CafeId"" = {0}",
            IntegrationTestFixtures.DemoCafeId);
    }
}
