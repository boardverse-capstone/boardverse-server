using BoardVerse.Core.DTOs.Match;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class MatchResultServiceTests
{
    private static readonly Guid LobbyId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01");
    private static readonly Guid GameId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Player1 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
    private static readonly Guid Player2 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd02");

    [Fact]
    public async Task SubmitMatchResultAsync_FirstSubmission_ReturnsAwaitingConsensus()
    {
        var repo = new Mock<IMatchResultRepository>();
        var config = new Mock<ISystemConfigurationProvider>();
        var lobby = BuildLobby(LobbyStatus.InProgress);

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId)).ReturnsAsync(lobby);
        repo.Setup(r => r.GameSupportsMatchResultsAsync(GameId)).ReturnsAsync(true);
        repo.Setup(r => r.GetFinalizedHistoryAsync(LobbyId)).ReturnsAsync((MatchHistory?)null);
        repo.Setup(r => r.GetSubmissionAsync(LobbyId, Player1)).ReturnsAsync((MatchResult?)null);
        repo.Setup(r => r.GetSubmissionsAsync(LobbyId))
            .ReturnsAsync([BuildSubmission(Player1, MatchOutcome.Win)]);

        var service = new MatchResultService(repo.Object, config.Object);
        var result = await service.SubmitMatchResultAsync(Player1, new SubmitMatchResultRequestDto
        {
            LobbyId = LobbyId,
            Outcome = MatchOutcome.Win
        });

        Assert.Equal(MatchConsensusStatus.AwaitingSubmissions, result.ConsensusStatus);
        Assert.Equal(1, result.SubmittedCount);
        Assert.Equal(2, result.RequiredCount);
        repo.Verify(r => r.AddSubmissionAsync(It.IsAny<MatchResult>()), Times.Once);
    }

    [Fact]
    public async Task SubmitMatchResultAsync_ConsensusReached_FinalizesAndUpdatesElo()
    {
        var repo = new Mock<IMatchResultRepository>();
        var config = new Mock<ISystemConfigurationProvider>();
        var lobby = BuildLobby(LobbyStatus.InProgress);

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId)).ReturnsAsync(lobby);
        repo.Setup(r => r.GameSupportsMatchResultsAsync(GameId)).ReturnsAsync(true);
        repo.Setup(r => r.GetFinalizedHistoryAsync(LobbyId)).ReturnsAsync((MatchHistory?)null);
        repo.Setup(r => r.GetSubmissionAsync(LobbyId, Player2)).ReturnsAsync((MatchResult?)null);
        repo.Setup(r => r.GetSubmissionsAsync(LobbyId))
            .ReturnsAsync(
            [
                BuildSubmission(Player1, MatchOutcome.Win),
                BuildSubmission(Player2, MatchOutcome.Loss)
            ]);
        repo.Setup(r => r.GetProfileForUpdateAsync(Player1))
            .ReturnsAsync(new UserProfile { UserId = Player1, GlobalElo = 1200, KarmaPoints = 100 });
        repo.Setup(r => r.GetProfileForUpdateAsync(Player2))
            .ReturnsAsync(new UserProfile { UserId = Player2, GlobalElo = 1200, KarmaPoints = 100 });
        config.Setup(c => c.GetIntAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(32);

        var service = new MatchResultService(repo.Object, config.Object);
        var result = await service.SubmitMatchResultAsync(Player2, new SubmitMatchResultRequestDto
        {
            LobbyId = LobbyId,
            Outcome = MatchOutcome.Loss
        });

        Assert.Equal(MatchConsensusStatus.Finalized, result.ConsensusStatus);
        Assert.NotNull(result.MatchHistoryId);
        Assert.Equal(2, result.EloUpdates!.Count);
        repo.Verify(r => r.AddMatchHistoryAsync(It.IsAny<MatchHistory>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SubmitMatchResultAsync_OpenLobby_ThrowsBadRequest()
    {
        var repo = new Mock<IMatchResultRepository>();
        var lobby = BuildLobby(LobbyStatus.Open);

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId)).ReturnsAsync(lobby);

        var service = new MatchResultService(repo.Object, Mock.Of<ISystemConfigurationProvider>());

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.SubmitMatchResultAsync(Player1, new SubmitMatchResultRequestDto
            {
                LobbyId = LobbyId,
                Outcome = MatchOutcome.Win
            }));
    }

    [Fact]
    public async Task GetMatchResultStatusAsync_NonMember_ThrowsForbidden()
    {
        var outsider = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var repo = new Mock<IMatchResultRepository>();
        var lobby = BuildLobby(LobbyStatus.InProgress);

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId)).ReturnsAsync(lobby);

        var service = new MatchResultService(repo.Object, Mock.Of<ISystemConfigurationProvider>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetMatchResultStatusAsync(outsider, LobbyId));
    }

    private static Lobby BuildLobby(LobbyStatus status) =>
        new()
        {
            Id = LobbyId,
            GameTemplateId = GameId,
            Status = status,
            GameTemplate = new GameTemplate
            {
                Id = GameId,
                Name = "Catan",
                MinPlayers = 2,
                MaxPlayers = 4,
                PlayTime = 60
            },
            Members =
            [
                new LobbyMember { UserId = Player1, IsActive = true },
                new LobbyMember { UserId = Player2, IsActive = true }
            ]
        };

    private static MatchResult BuildSubmission(Guid userId, MatchOutcome outcome) =>
        new()
        {
            Id = Guid.NewGuid(),
            LobbyId = LobbyId,
            UserId = userId,
            Outcome = outcome,
            SubmittedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
}
