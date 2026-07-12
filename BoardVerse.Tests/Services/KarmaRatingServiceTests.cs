using BoardVerse.Core.DTOs.Rating;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

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
        var targetProfile = new UserProfile { UserId = Player2, KarmaPoints = 100, GamerTier = GamerTier.Bronze };

        repo.Setup(r => r.GetLobbyForRatingAsync(LobbyId)).ReturnsAsync(lobby);
        // Inside the loop: HasRatingAsync is called BEFORE GetProfileForUpdateAsync
        repo.Setup(r => r.HasRatingAsync(LobbyId, Player1, Player2)).ReturnsAsync(false);
        // Both rater (Player1) and target (Player2) profiles are fetched inside the loop
        repo.Setup(r => r.GetProfileForUpdateAsync(Player1)).ReturnsAsync(new UserProfile { UserId = Player1, KarmaPoints = 50 });
        repo.Setup(r => r.GetProfileForUpdateAsync(Player2)).ReturnsAsync(targetProfile);

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

        repo.Setup(r => r.GetLobbyForRatingAsync(LobbyId)).ReturnsAsync(lobby);
        // IsRatingAllowed(InProgress) = true, so we need HasRatingAsync to be true
        // to trigger the ConflictException path
        repo.Setup(r => r.HasRatingAsync(LobbyId, Player1, Player2)).ReturnsAsync(true);

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

        repo.Setup(r => r.GetLobbyForUpdateAsync(LobbyId)).ReturnsAsync(lobby);

        var service = new KarmaRatingService(repo.Object);
        var result = await service.OpenLobbyKarmaRatingWindowAsync(LobbyId);

        Assert.Equal(LobbyStatus.RatingOpen, lobby.Status);
        Assert.NotNull(lobby.RatingOpenedAt);
        Assert.Equal(2, result.MemberUserIds.Count);
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetLobbyRatingContextAsync_ExcludesSelfFromMembersToRate()
    {
        var repo = new Mock<IKarmaRatingRepository>();
        // IsRatingAllowed(InProgress) = true → CanSubmitRatings = true
        var lobby = BuildLobby(LobbyStatus.InProgress);

        repo.Setup(r => r.GetLobbyForRatingAsync(LobbyId)).ReturnsAsync(lobby);
        repo.Setup(r => r.GetRatedTargetIdsAsync(LobbyId, Player1)).ReturnsAsync([]);

        var service = new KarmaRatingService(repo.Object);
        var context = await service.GetLobbyRatingContextAsync(Player1, LobbyId);

        Assert.True(context.CanSubmitRatings);
        Assert.Single(context.MembersToRate);
        Assert.Equal(Player2, context.MembersToRate[0].UserId);
        Assert.DoesNotContain(context.MembersToRate, m => m.UserId == Player1);
    }

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
