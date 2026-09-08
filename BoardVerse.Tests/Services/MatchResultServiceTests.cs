using BoardVerse.Core.DTOs.Match;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Moq;

using System.Threading;
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

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);
        repo.Setup(r => r.GameSupportsMatchResultsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.GetFinalizedHistoryAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync((MatchHistory?)null);
        repo.Setup(r => r.GetSubmissionAsync(LobbyId, Player1, It.IsAny<CancellationToken>())).ReturnsAsync((MatchResult?)null);
        repo.Setup(r => r.GetSubmissionsAsync(LobbyId, It.IsAny<CancellationToken>()))
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
        repo.Verify(r => r.AddSubmissionAsync(It.IsAny<MatchResult>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Skip = "MatchResultService uses DbContext transactions which require real database - integration tests only")]
    public async Task SubmitMatchResultAsync_ConsensusReached_FinalizesAndUpdatesElo()
    {
        var repo = new Mock<IMatchResultRepository>();
        var config = new Mock<ISystemConfigurationProvider>();
        var lobby = BuildLobby(LobbyStatus.InProgress);

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);
        repo.Setup(r => r.GameSupportsMatchResultsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.GetFinalizedHistoryAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync((MatchHistory?)null);
        repo.Setup(r => r.GetSubmissionAsync(LobbyId, Player2, It.IsAny<CancellationToken>())).ReturnsAsync((MatchResult?)null);
        repo.Setup(r => r.GetSubmissionsAsync(LobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                BuildSubmission(Player1, MatchOutcome.Win),
                BuildSubmission(Player2, MatchOutcome.Loss)
            ]);
        repo.Setup(r => r.GetProfileForUpdateAsync(Player1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfile { UserId = Player1, GlobalElo = 1200, KarmaPoints = 100 });
        repo.Setup(r => r.GetProfileForUpdateAsync(Player2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfile { UserId = Player2, GlobalElo = 1200, KarmaPoints = 100 });
        config.Setup(c => c.GetIntAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(32);

        var service = new MatchResultService(repo.Object, config.Object);
        var result = await service.SubmitMatchResultAsync(Player2, new SubmitMatchResultRequestDto
        {
            LobbyId = LobbyId,
            Outcome = MatchOutcome.Loss
        });

        Assert.Equal(MatchConsensusStatus.Finalized, result.ConsensusStatus);
        Assert.NotNull(result.MatchHistoryId);
        Assert.Equal(2, result.EloUpdates!.Count);
        repo.Verify(r => r.AddMatchHistoryAsync(It.IsAny<MatchHistory>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SubmitMatchResultAsync_OpenLobby_ThrowsBadRequest()
    {
        var repo = new Mock<IMatchResultRepository>();
        var lobby = BuildLobby(LobbyStatus.Open);

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);

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

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);

        var service = new MatchResultService(repo.Object, Mock.Of<ISystemConfigurationProvider>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetMatchResultStatusAsync(outsider, LobbyId));
    }

    /// <summary>
    /// GAP-1 regression: sau khi POS đóng phiên, ReservationService.MarkLobbyMembersInactive
    /// set IsActive=false + Status=LobbyTerminated cho members. Member vẫn phải đọc được
    /// status match result, không throw 403.
    /// </summary>
    [Fact]
    public async Task GetMatchResultStatusAsync_PostSessionClosedLobbyWithTerminatedMembers_DoesNotThrow403()
    {
        var repo = new Mock<IMatchResultRepository>();
        var lobby = BuildLobbyWithTerminatedMembers(LobbyStatus.Closed);

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);
        repo.Setup(r => r.GameSupportsMatchResultsAsync(GameId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.GetFinalizedHistoryAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync((MatchHistory?)null);
        repo.Setup(r => r.GetSubmissionsAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var service = new MatchResultService(repo.Object, Mock.Of<ISystemConfigurationProvider>());

        var result = await service.GetMatchResultStatusAsync(Player1, LobbyId);

        Assert.NotNull(result);
        Assert.Equal(LobbyId, result.LobbyId);
        Assert.Equal(2, result.RequiredCount); // Cả 2 terminated members đều count vào required
    }

    /// <summary>
    /// GAP-1 regression: Kicked member không được phép submit match result,
    /// dù lobby đã ở trạng thái terminal.
    /// </summary>
    [Fact]
    public async Task SubmitMatchResultAsync_KickedMember_ThrowsForbidden()
    {
        var repo = new Mock<IMatchResultRepository>();
        var lobby = BuildLobbyWithTerminatedMembers(LobbyStatus.Closed);
        // Đánh dấu Player1 là Kicked
        lobby.Members.First(m => m.UserId == Player1).Status = LobbyMemberStatus.Kicked;

        repo.Setup(r => r.GetLobbyForMatchAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);

        var service = new MatchResultService(repo.Object, Mock.Of<ISystemConfigurationProvider>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SubmitMatchResultAsync(Player1, new SubmitMatchResultRequestDto
            {
                LobbyId = LobbyId,
                Outcome = MatchOutcome.Win
            }));
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

    /// <summary>
    /// GAP-1 helper: lobby sau khi ReservationService.MarkLobbyMembersInactive chạy.
    /// Members có IsActive=false + Status=LobbyTerminated (giả lập post-POS-close state).
    /// </summary>
    private static Lobby BuildLobbyWithTerminatedMembers(LobbyStatus status)
    {
        var lobby = BuildLobby(status);
        foreach (var member in lobby.Members)
        {
            member.IsActive = false;
            member.Status = LobbyMemberStatus.LobbyTerminated;
            member.LeftAt = DateTime.UtcNow;
            member.User ??= new User
            {
                Id = member.UserId,
                Username = $"player_{member.UserId.ToString("N")[..6]}",
                Email = $"player_{member.UserId:N}@test.local"
            };
        }
        return lobby;
    }

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
