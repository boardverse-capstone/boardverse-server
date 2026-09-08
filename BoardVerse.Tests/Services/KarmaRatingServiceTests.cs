using BoardVerse.Core.DTOs.Rating;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

using System.Threading;
namespace BoardVerse.Tests.Services;

public class KarmaRatingServiceTests
{
    private static readonly Guid LobbyId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee02");
    private static readonly Guid Player1 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
    private static readonly Guid Player2 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd02");

    [Fact]
    public async Task SubmitKarmaRatingsAsync_AppliesTagDeltaToTargetProfile()
    {
        var repo = new Mock<IKarmaRatingRepository>();
        var lobby = BuildLobby(LobbyStatus.InProgress);
        var targetProfile = new UserProfile { UserId = Player2, KarmaPoints = 95, GamerTier = GamerTier.Gold };

        repo.Setup(r => r.GetLobbyForRatingAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);
        // Inside the loop: HasRatingAsync is called BEFORE GetProfileForUpdateAsync
        repo.Setup(r => r.HasRatingAsync(LobbyId, Player1, Player2, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        // Both rater (Player1) and target (Player2) profiles are fetched inside the loop
        repo.Setup(r => r.GetProfileForUpdateAsync(Player1, It.IsAny<CancellationToken>())).ReturnsAsync(new UserProfile { UserId = Player1, KarmaPoints = 50 });
        repo.Setup(r => r.GetProfileForUpdateAsync(Player2, It.IsAny<CancellationToken>())).ReturnsAsync(targetProfile);

        var service = new KarmaRatingService(repo.Object);
        var result = await service.SubmitKarmaRatingsAsync(Player1, new SubmitKarmaRatingsRequestDto
        {
            LobbyId = LobbyId,
            Ratings =
            [
                new KarmaRatingEntryDto
                {
                    TargetUserId = Player2,
                    Tags = [KarmaRatingTag.Friendly, KarmaRatingTag.OnTime]
                }
            ]
        });

        // Assert on the response DTO (built inside the loop, before SaveChangesAsync)
        Assert.Single(result.AppliedRatings);
        Assert.Equal(Player2, result.AppliedRatings[0].TargetUserId);
    }

    [Fact]
    public async Task SubmitKarmaRatingsAsync_WhenLobbyNotOpen_ThrowsBadRequest()
    {
        var repo = new Mock<IKarmaRatingRepository>();
        var lobby = BuildLobby(LobbyStatus.InProgress);

        repo.Setup(r => r.GetLobbyForRatingAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);
        // IsRatingAllowed(InProgress) = true, so we need HasRatingAsync to be true
        // to trigger the ConflictException path
        repo.Setup(r => r.HasRatingAsync(LobbyId, Player1, Player2, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var service = new KarmaRatingService(repo.Object);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.SubmitKarmaRatingsAsync(Player1, new SubmitKarmaRatingsRequestDto
            {
                LobbyId = LobbyId,
                Ratings =
                [
                    new KarmaRatingEntryDto
                    {
                        TargetUserId = Player2,
                        Tags = [KarmaRatingTag.Friendly]
                    }
                ]
            }));
    }

    [Fact]
    public async Task OpenLobbyKarmaRatingWindowAsync_TransitionsToRatingOpen()
    {
        var repo = new Mock<IKarmaRatingRepository>();
        var lobby = BuildLobby(LobbyStatus.Closed);

        repo.Setup(r => r.GetLobbyForUpdateAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);

        var service = new KarmaRatingService(repo.Object);
        var result = await service.OpenLobbyKarmaRatingWindowAsync(LobbyId);

        Assert.Equal(LobbyStatus.RatingOpen, lobby.Status);
        Assert.NotNull(lobby.RatingOpenedAt);
        Assert.Equal(2, result.MemberUserIds.Count);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLobbyRatingContextAsync_ExcludesSelfFromMembersToRate()
    {
        var repo = new Mock<IKarmaRatingRepository>();
        // IsRatingAllowed(InProgress) = true → CanSubmitRatings = true
        var lobby = BuildLobby(LobbyStatus.InProgress);

        repo.Setup(r => r.GetLobbyForRatingAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);
        repo.Setup(r => r.GetRatedTargetIdsAsync(LobbyId, Player1, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var service = new KarmaRatingService(repo.Object);
        var context = await service.GetLobbyRatingContextAsync(Player1, LobbyId);

        Assert.True(context.CanSubmitRatings);
        Assert.Single(context.MembersToRate);
        Assert.Equal(Player2, context.MembersToRate[0].UserId);
        Assert.DoesNotContain(context.MembersToRate, m => m.UserId == Player1);
    }

    [Fact]
    public async Task OpenLobbyKarmaRatingWindowAsync_ReactivatesLobbyTerminatedMembers()
    {
        // Regression: bug khi lobby đi qua Closed trước (ReservationService.MarkLobbyMembersInactive
        // set IsActive=false + Status=LobbyTerminated). Mở rating window phải re-activate để
        // KarmaRatingRepository.GetLobbyForRatingAsync (filter Members.Where(IsActive)) trả về
        // collection không rỗng — nếu không thì host lẫn members đều bị 403 "không phải thành viên".
        var repo = new Mock<IKarmaRatingRepository>();
        var lobby = BuildClosedLobbyWithTerminatedMembers();

        repo.Setup(r => r.GetLobbyForUpdateAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);

        var service = new KarmaRatingService(repo.Object);
        var result = await service.OpenLobbyKarmaRatingWindowAsync(LobbyId);

        Assert.Equal(LobbyStatus.RatingOpen, lobby.Status);
        Assert.Equal(2, result.MemberUserIds.Count);
        Assert.All(lobby.Members, m =>
        {
            Assert.True(m.IsActive, $"Member {m.UserId} phải được re-activate khi mở rating window.");
            Assert.Equal(LobbyMemberStatus.LobbyTerminated, m.Status);
        });
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenLobbyKarmaRatingWindowAsync_DoesNotReactivateKickedOrLeftMembers()
    {
        // Edge case: member bị kick hoặc tự rời trước khi lobby close → không được phép rate.
        var repo = new Mock<IKarmaRatingRepository>();
        var lobby = BuildClosedLobbyWithTerminatedMembers();
        lobby.Members.Add(new LobbyMember
        {
            UserId = Guid.NewGuid(),
            IsActive = false,
            IsHost = false,
            Status = LobbyMemberStatus.Left,
            User = new User { Id = Guid.NewGuid(), Username = "leaver", Email = "leaver@test.dev" }
        });

        repo.Setup(r => r.GetLobbyForUpdateAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);

        var service = new KarmaRatingService(repo.Object);
        var result = await service.OpenLobbyKarmaRatingWindowAsync(LobbyId);

        Assert.Equal(2, result.MemberUserIds.Count);
        Assert.DoesNotContain(result.MemberUserIds, id =>
            lobby.Members.Any(m => m.UserId == id && m.Status == LobbyMemberStatus.Left));
    }

    [Fact]
    public async Task SubmitKarmaRatingsAsync_HostCanRate_WhenLobbyReactivatedAfterClosed()
    {
        // End-to-end: mô phỏng flow thực tế — lobby đã Closed (members IsActive=false),
        // sau đó mở rating window, host gọi SubmitKarmaRatings. Trước fix, host bị 403.
        var repo = new Mock<IKarmaRatingRepository>();
        var lobby = BuildClosedLobbyWithTerminatedMembers();
        // Sau khi mở rating window (giả lập):
        foreach (var m in lobby.Members.Where(m => m.Status == LobbyMemberStatus.LobbyTerminated))
        {
            m.IsActive = true;
        }
        lobby.Status = LobbyStatus.RatingOpen;
        lobby.RatingOpenedAt = DateTime.UtcNow;

        var targetProfile = new UserProfile { UserId = Player2, KarmaPoints = 95, GamerTier = GamerTier.Gold };

        repo.Setup(r => r.GetLobbyForRatingAsync(LobbyId, It.IsAny<CancellationToken>())).ReturnsAsync(lobby);
        repo.Setup(r => r.HasRatingAsync(LobbyId, Player1, Player2, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repo.Setup(r => r.GetProfileForUpdateAsync(Player1, It.IsAny<CancellationToken>())).ReturnsAsync(new UserProfile { UserId = Player1, KarmaPoints = 50 });
        repo.Setup(r => r.GetProfileForUpdateAsync(Player2, It.IsAny<CancellationToken>())).ReturnsAsync(targetProfile);

        var service = new KarmaRatingService(repo.Object);
        var result = await service.SubmitKarmaRatingsAsync(Player1, new SubmitKarmaRatingsRequestDto
        {
            LobbyId = LobbyId,
            Ratings =
            [
                new KarmaRatingEntryDto
                {
                    TargetUserId = Player2,
                    Tags = [KarmaRatingTag.Friendly]
                }
            ]
        });

        Assert.Single(result.AppliedRatings);
    }

    private static Lobby BuildClosedLobbyWithTerminatedMembers() =>
        new Lobby
        {
            Id = LobbyId,
            GameTemplateId = Guid.NewGuid(),
            Status = LobbyStatus.Closed,
            ClosedAt = DateTime.UtcNow,
            Members = new List<LobbyMember>
            {
                new LobbyMember
                {
                    UserId = Player1,
                    IsActive = false,            // ← giả lập MarkLobbyMembersInactive đã chạy
                    IsHost = true,
                    Status = LobbyMemberStatus.LobbyTerminated,
                    User = new User { Id = Player1, Username = "jonny", Email = "jonny@test.dev" }
                },
                new LobbyMember
                {
                    UserId = Player2,
                    IsActive = false,
                    IsHost = false,
                    Status = LobbyMemberStatus.LobbyTerminated,
                    User = new User { Id = Player2, Username = "player2", Email = "p2@test.dev" }
                }
            }
        };

    private static Lobby BuildLobby(LobbyStatus status) =>
        new Lobby
        {
            Id = LobbyId,
            GameTemplateId = Guid.NewGuid(),
            Status = status,
            Members = new List<LobbyMember>
            {
                new LobbyMember
                {
                    UserId = Player1,
                    IsActive = true,
                    IsHost = true,
                    User = new User { Id = Player1, Username = "player1", Email = "p1@test.dev" }
                },
                new LobbyMember
                {
                    UserId = Player2,
                    IsActive = true,
                    IsHost = false,
                    User = new User { Id = Player2, Username = "player2", Email = "p2@test.dev" }
                }
            }
        };
}
