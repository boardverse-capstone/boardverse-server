using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class FriendServiceTests
{
    private readonly Mock<IFriendshipRepository> _friendshipRepo = new();
    private readonly Mock<IUserManagementRepository> _userRepo = new();
    private readonly Mock<ILobbyMemberRepository> _lobbyMemberRepo = new();
    private readonly Mock<IFriendNoteService> _friendNoteService = new();
    private readonly Mock<ILobbyInviteRepository> _lobbyInviteRepo = new();
    private readonly Mock<ILogger<FriendService>> _logger = new();

    private FriendService CreateService() => new(
        _friendshipRepo.Object,
        _userRepo.Object,
        _lobbyMemberRepo.Object,
        _friendNoteService.Object,
        _lobbyInviteRepo.Object,
        _logger.Object);

    private static User BuildUser(Guid id, string username = "alice", UserRole role = UserRole.Player, bool active = true)
    {
        return new User
        {
            Id = id,
            Username = username,
            Email = $"{username}@boardverse.test",
            Role = role,
            IsActive = active,
            AccountStatus = UserAccountStatus.Active,
            Profile = new UserProfile
            {
                UserId = id,
                KarmaPoints = 100,
                IsFriendListPublic = true,
                AcceptFriendRequestsFrom = "Everyone",
                FriendLimit = 0
            }
        };
    }

    private static Friendship BuildFriendship(
        Guid id,
        Guid requesterId,
        Guid addresseeId,
        FriendshipStatus status = FriendshipStatus.Pending,
        DateTime? createdAt = null)
    {
        return new Friendship
        {
            Id = id,
            RequesterId = requesterId,
            AddresseeId = addresseeId,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-10),
            Requester = BuildUser(requesterId, "requester"),
            Addressee = BuildUser(addresseeId, "addressee")
        };
    }

    #region SendFriendRequestAsync

    [Fact]
    public async Task SendFriendRequestAsync_WhenAddresseeIsSelf_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.SendFriendRequestAsync(userId, new SendFriendRequestDto { AddresseeId = userId }));
    }

    [Fact]
    public async Task SendFriendRequestAsync_WhenAddresseeNotFound_ThrowsNotFound()
    {
        var requesterId = Guid.NewGuid();
        var addresseeId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(addresseeId)).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.SendFriendRequestAsync(requesterId, new SendFriendRequestDto { AddresseeId = addresseeId }));
    }

    [Fact]
    public async Task SendFriendRequestAsync_WhenAddresseeInactive_ThrowsBadRequest()
    {
        var requesterId = Guid.NewGuid();
        var addresseeId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(addresseeId)).ReturnsAsync(BuildUser(addresseeId, active: false));
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(addresseeId)).ReturnsAsync(BuildUser(addresseeId, active: false));

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.SendFriendRequestAsync(requesterId, new SendFriendRequestDto { AddresseeId = addresseeId }));
    }

    [Fact]
    public async Task SendFriendRequestAsync_AlreadyFriends_ThrowsConflict()
    {
        var requesterId = Guid.NewGuid();
        var addresseeId = Guid.NewGuid();
        var existing = BuildFriendship(Guid.NewGuid(), requesterId, addresseeId, FriendshipStatus.Accepted);

        _userRepo.Setup(r => r.GetByIdAsync(addresseeId)).ReturnsAsync(BuildUser(addresseeId));
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(addresseeId)).ReturnsAsync(BuildUser(addresseeId));
        _friendshipRepo.Setup(r => r.GetByUserAsync(requesterId, FriendshipStatus.Pending)).ReturnsAsync(new List<Friendship>());
        _friendshipRepo.Setup(r => r.GetFriendUserIdsAsync(requesterId)).ReturnsAsync(new List<Guid>());
        _friendshipRepo.Setup(r => r.GetByPairAsync(requesterId, addresseeId)).ReturnsAsync(existing);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.SendFriendRequestAsync(requesterId, new SendFriendRequestDto { AddresseeId = addresseeId }));
    }

    [Fact]
    public async Task SendFriendRequestAsync_PendingAlreadyExists_ThrowsConflict()
    {
        var requesterId = Guid.NewGuid();
        var addresseeId = Guid.NewGuid();
        var existing = BuildFriendship(Guid.NewGuid(), requesterId, addresseeId, FriendshipStatus.Pending);

        _userRepo.Setup(r => r.GetByIdAsync(addresseeId)).ReturnsAsync(BuildUser(addresseeId));
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(addresseeId)).ReturnsAsync(BuildUser(addresseeId));
        _friendshipRepo.Setup(r => r.GetByUserAsync(requesterId, FriendshipStatus.Pending)).ReturnsAsync(new List<Friendship>());
        _friendshipRepo.Setup(r => r.GetFriendUserIdsAsync(requesterId)).ReturnsAsync(new List<Guid>());
        _friendshipRepo.Setup(r => r.GetByPairAsync(requesterId, addresseeId)).ReturnsAsync(existing);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.SendFriendRequestAsync(requesterId, new SendFriendRequestDto { AddresseeId = addresseeId }));
    }

    [Fact]
    public async Task SendFriendRequestAsync_RateLimitExceeded_ThrowsTooManyRequests()
    {
        var requesterId = Guid.NewGuid();
        var addresseeId = Guid.NewGuid();
        var sent = Enumerable.Range(0, 20).Select(_ => BuildFriendship(Guid.NewGuid(), requesterId, Guid.NewGuid(), FriendshipStatus.Pending, DateTime.UtcNow.AddMinutes(-5))).ToList();

        _userRepo.Setup(r => r.GetByIdAsync(addresseeId)).ReturnsAsync(BuildUser(addresseeId));
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(addresseeId)).ReturnsAsync(BuildUser(addresseeId));
        _friendshipRepo.Setup(r => r.GetByUserAsync(requesterId, FriendshipStatus.Pending)).ReturnsAsync(sent);

        var svc = CreateService();

        await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            svc.SendFriendRequestAsync(requesterId, new SendFriendRequestDto { AddresseeId = addresseeId }));
    }

    [Fact]
    public async Task SendFriendRequestAsync_PrivacyFriendsOfFriendsNotAllowed_ThrowsForbidden()
    {
        var requesterId = Guid.NewGuid();
        var addresseeId = Guid.NewGuid();
        var addressee = BuildUser(addresseeId);
        addressee.Profile!.AcceptFriendRequestsFrom = "FriendsOfFriends";

        _userRepo.Setup(r => r.GetByIdAsync(addresseeId)).ReturnsAsync(addressee);
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(addresseeId)).ReturnsAsync(addressee);
        _friendshipRepo.Setup(r => r.GetByUserAsync(requesterId, FriendshipStatus.Pending)).ReturnsAsync(new List<Friendship>());
        _friendshipRepo.Setup(r => r.GetFriendUserIdsAsync(requesterId)).ReturnsAsync(new List<Guid>());
        _friendshipRepo.Setup(r => r.GetByPairAsync(requesterId, addresseeId)).ReturnsAsync((Friendship?)null);
        _friendshipRepo.Setup(r => r.GetFriendUserIdsAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Guid>());

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SendFriendRequestAsync(requesterId, new SendFriendRequestDto { AddresseeId = addresseeId }));
    }

    [Fact]
    public async Task SendFriendRequestAsync_ValidRequest_PersistsAndReturnsPending()
    {
        var requesterId = Guid.NewGuid();
        var addresseeId = Guid.NewGuid();
        var addressee = BuildUser(addresseeId, "alice");
        _userRepo.Setup(r => r.GetByIdAsync(addresseeId)).ReturnsAsync(addressee);
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(addresseeId)).ReturnsAsync(addressee);
        _friendshipRepo.Setup(r => r.GetByUserAsync(requesterId, FriendshipStatus.Pending)).ReturnsAsync(new List<Friendship>());
        _friendshipRepo.Setup(r => r.GetFriendUserIdsAsync(requesterId)).ReturnsAsync(new List<Guid>());
        _friendshipRepo.Setup(r => r.GetByPairAsync(requesterId, addresseeId)).ReturnsAsync((Friendship?)null);

        Friendship? captured = null;
        _friendshipRepo.Setup(r => r.AddAsync(It.IsAny<Friendship>()))
            .Callback<Friendship>(f =>
            {
                captured = f;
                f.Requester = BuildUser(requesterId, "requester");
                f.Addressee = addressee;
            })
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        var result = await svc.SendFriendRequestAsync(requesterId, new SendFriendRequestDto
        {
            AddresseeId = addresseeId,
            Message = "Chơi Catan nhé"
        });

        Assert.NotNull(captured);
        Assert.Equal(requesterId, captured!.RequesterId);
        Assert.Equal(addresseeId, captured.AddresseeId);
        Assert.Equal(FriendshipStatus.Pending, captured.Status);
        Assert.Equal("Chơi Catan nhé", captured.Message);
        _friendshipRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal(FriendshipStatus.Pending.ToString(), result.Status);
        Assert.Equal(addresseeId, result.OtherUserId);
        Assert.True(result.IsRequester);
    }

    #endregion

    #region AcceptFriendRequestAsync

    [Fact]
    public async Task AcceptFriendRequestAsync_WhenFriendshipNotFound_ThrowsNotFound()
    {
        var userId = Guid.NewGuid();
        _friendshipRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Friendship?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.AcceptFriendRequestAsync(userId, Guid.NewGuid()));
    }

    [Fact]
    public async Task AcceptFriendRequestAsync_WhenNotAddressee_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), Guid.NewGuid(), other);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.AcceptFriendRequestAsync(userId, f.Id));
    }

    [Fact]
    public async Task AcceptFriendRequestAsync_WhenAlreadyAccepted_ThrowsConflict()
    {
        var userId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), requesterId, userId, FriendshipStatus.Accepted);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.AcceptFriendRequestAsync(userId, f.Id));
    }

    [Fact]
    public async Task AcceptFriendRequestAsync_WhenRequesterInactive_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), requesterId, userId);
        f.Requester = BuildUser(requesterId, active: false);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);
        _userRepo.Setup(r => r.GetByIdAsync(requesterId)).ReturnsAsync(BuildUser(requesterId, active: false));

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() => svc.AcceptFriendRequestAsync(userId, f.Id));
    }

    [Fact]
    public async Task AcceptFriendRequestAsync_WhenFriendLimitReached_ThrowsConflict()
    {
        var userId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), requesterId, userId);
        var me = BuildUser(userId, "me");
        me.Profile!.FriendLimit = 100;
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);
        _userRepo.Setup(r => r.GetByIdAsync(requesterId)).ReturnsAsync(BuildUser(requesterId, active: true));
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(me);
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(me);
        _friendshipRepo.Setup(r => r.GetByPairAsync(userId, requesterId)).ReturnsAsync((Friendship?)null);
        _friendshipRepo.Setup(r => r.CountFriendsAsync(userId)).ReturnsAsync(100);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.AcceptFriendRequestAsync(userId, f.Id));
    }

    [Fact]
    public async Task AcceptFriendRequestAsync_ValidRequest_SetsAccepted()
    {
        var userId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), requesterId, userId);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);
        _userRepo.Setup(r => r.GetByIdAsync(requesterId)).ReturnsAsync(BuildUser(requesterId, active: true));
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(BuildUser(userId, "me"));
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(userId)).ReturnsAsync(BuildUser(userId, "me"));
        _friendshipRepo.Setup(r => r.GetByPairAsync(userId, requesterId)).ReturnsAsync((Friendship?)null);
        _friendshipRepo.Setup(r => r.CountFriendsAsync(userId)).ReturnsAsync(0);

        var svc = CreateService();

        var result = await svc.AcceptFriendRequestAsync(userId, f.Id);

        Assert.Equal(FriendshipStatus.Accepted, f.Status);
        Assert.NotNull(f.AcceptedAt);
        _friendshipRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal(FriendshipStatus.Accepted.ToString(), result.Status);
    }

    #endregion

    #region DeclineFriendRequestAsync

    [Fact]
    public async Task DeclineFriendRequestAsync_WhenNotAddressee_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeclineFriendRequestAsync(userId, f.Id));
    }

    [Fact]
    public async Task DeclineFriendRequestAsync_WhenNotPending_ThrowsConflict()
    {
        var userId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), requesterId, userId, FriendshipStatus.Accepted);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.DeclineFriendRequestAsync(userId, f.Id));
    }

    [Fact]
    public async Task DeclineFriendRequestAsync_ValidRequest_SetsRemoved()
    {
        var userId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), requesterId, userId, FriendshipStatus.Pending);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await svc.DeclineFriendRequestAsync(userId, f.Id);

        Assert.Equal(FriendshipStatus.Removed, f.Status);
        _friendshipRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region RemoveFriendshipAsync

    [Fact]
    public async Task RemoveFriendshipAsync_WhenNeitherParty_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FriendshipStatus.Accepted);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.RemoveFriendshipAsync(userId, f.Id));
    }

    [Fact]
    public async Task RemoveFriendshipAsync_WhenNotAccepted_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), userId, other, FriendshipStatus.Pending);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() => svc.RemoveFriendshipAsync(userId, f.Id));
    }

    [Fact]
    public async Task RemoveFriendshipAsync_ValidRequest_CancelsPendingLobbyInvites()
    {
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), userId, other, FriendshipStatus.Accepted);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);
        _lobbyInviteRepo.Setup(r => r.CancelPendingBetweenAsync(userId, other)).ReturnsAsync(new List<LobbyInvite> { new() { Id = Guid.NewGuid() } });

        var svc = CreateService();

        await svc.RemoveFriendshipAsync(userId, f.Id);

        Assert.Equal(FriendshipStatus.Removed, f.Status);
        _friendshipRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        _lobbyInviteRepo.Verify(r => r.CancelPendingBetweenAsync(userId, other), Times.Once);
    }

    #endregion

    #region Block / Unblock

    [Fact]
    public async Task BlockUserAsync_TargetNotFound_ThrowsNotFound()
    {
        var userId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.BlockUserAsync(userId, targetId));
    }

    [Fact]
    public async Task BlockUserAsync_TargetIsAdmin_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync(BuildUser(targetId, "admin", UserRole.Admin));

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.BlockUserAsync(userId, targetId));
    }

    [Fact]
    public async Task BlockUserAsync_SelfBlock_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() => svc.BlockUserAsync(userId, userId));
    }

    [Fact]
    public async Task BlockUserAsync_NewBlock_CreatesBlockedRecord()
    {
        var userId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(targetId)).ReturnsAsync(BuildUser(targetId));
        _friendshipRepo.Setup(r => r.GetByPairAsync(userId, targetId)).ReturnsAsync((Friendship?)null);

        var svc = CreateService();

        await svc.BlockUserAsync(userId, targetId);

        _friendshipRepo.Verify(r => r.AddAsync(It.Is<Friendship>(f =>
            f.RequesterId == userId &&
            f.AddresseeId == targetId &&
            f.Status == FriendshipStatus.Blocked)), Times.Once);
        _friendshipRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UnblockUserAsync_NoExistingBlock_ThrowsNotFound()
    {
        var userId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _friendshipRepo.Setup(r => r.GetByPairAsync(userId, targetId)).ReturnsAsync((Friendship?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.UnblockUserAsync(userId, targetId));
    }

    [Fact]
    public async Task UnblockUserAsync_NotBlocker_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var blockerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var f = new Friendship { Id = Guid.NewGuid(), RequesterId = blockerId, AddresseeId = userId, Status = FriendshipStatus.Blocked };
        _friendshipRepo.Setup(r => r.GetByPairAsync(userId, targetId)).ReturnsAsync(f);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.UnblockUserAsync(userId, targetId));
    }

    [Fact]
    public async Task UnblockUserAsync_ValidBlocker_SetsRemovedAndSaves()
    {
        var userId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var f = new Friendship { Id = Guid.NewGuid(), RequesterId = userId, AddresseeId = targetId, Status = FriendshipStatus.Blocked };
        _friendshipRepo.Setup(r => r.GetByPairAsync(userId, targetId)).ReturnsAsync(f);

        var svc = CreateService();

        await svc.UnblockUserAsync(userId, targetId);

        Assert.Equal(FriendshipStatus.Removed, f.Status);
        _friendshipRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region Get / Search / Suggestions / Mutual / Activity

    [Fact]
    public async Task GetFriendsAsync_ReturnsAcceptedOnly()
    {
        var userId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var friendships = new List<Friendship>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RequesterId = userId,
                AddresseeId = friendId,
                Status = FriendshipStatus.Accepted,
                AcceptedAt = DateTime.UtcNow.AddDays(-3),
                UpdatedAt = DateTime.UtcNow.AddDays(-3),
                Addressee = BuildUser(friendId, "bob")
            }
        };
        _friendshipRepo.Setup(r => r.GetFriendsAsync(userId)).ReturnsAsync(friendships);

        var svc = CreateService();

        var result = await svc.GetFriendsAsync(userId);

        Assert.Single(result);
        Assert.Equal(friendId, result[0].UserId);
        Assert.Equal("bob", result[0].Username);
    }

    [Fact]
    public async Task SearchUsersAsync_DecoratesRelationshipWithMutualCount()
    {
        var meId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var found = new List<User> { BuildUser(otherId, "alice") };

        _userRepo.Setup(r => r.SearchByUsernameAsync("ali", meId, 20)).ReturnsAsync(found);
        _friendshipRepo.Setup(r => r.GetByPairAsync(meId, otherId)).ReturnsAsync((Friendship?)null);
        _friendshipRepo.Setup(r => r.CountMutualFriendsAsync(meId, otherId)).ReturnsAsync(3);

        var svc = CreateService();

        var result = await svc.SearchUsersAsync(meId, "ali", 20);

        Assert.Single(result);
        Assert.Equal(otherId, result[0].UserId);
        Assert.Null(result[0].FriendshipStatus);
        Assert.Equal(3, result[0].MutualFriendsCount);
    }

    [Fact]
    public async Task GetPendingReceivedRequestsAsync_ReturnsOnlyForAddressee()
    {
        var meId = Guid.NewGuid();
        var pending = new List<Friendship>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RequesterId = Guid.NewGuid(),
                AddresseeId = meId,
                Status = FriendshipStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
                Requester = BuildUser(Guid.NewGuid(), "bob")
            }
        };
        _friendshipRepo.Setup(r => r.GetByUserAsync(meId, FriendshipStatus.Pending)).ReturnsAsync(pending);

        var svc = CreateService();

        var result = await svc.GetPendingReceivedRequestsAsync(meId);

        Assert.Single(result);
        Assert.False(result[0].IsRequester);
    }

    [Fact]
    public async Task GetPendingSentRequestsAsync_ReturnsOnlyWhereRequesterIsMe()
    {
        var meId = Guid.NewGuid();
        var pending = new List<Friendship>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RequesterId = meId,
                AddresseeId = Guid.NewGuid(),
                Status = FriendshipStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Addressee = BuildUser(Guid.NewGuid(), "alice")
            }
        };
        _friendshipRepo.Setup(r => r.GetByUserAsync(meId, FriendshipStatus.Pending)).ReturnsAsync(pending);

        var svc = CreateService();

        var result = await svc.GetPendingSentRequestsAsync(meId);

        Assert.Single(result);
        Assert.True(result[0].IsRequester);
    }

    [Fact]
    public async Task GetMutualFriendsAsync_ReturnsIntersection()
    {
        var meId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var mutualIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var users = mutualIds.Select((id, i) => BuildUser(id, $"user{i}")).ToList();

        _friendshipRepo.Setup(r => r.GetMutualFriendIdsAsync(meId, otherId)).ReturnsAsync(mutualIds);
        _userRepo.Setup(r => r.GetByIdsAsync(mutualIds)).ReturnsAsync(users);
        _friendshipRepo.Setup(r => r.GetFriendsAsync(meId)).ReturnsAsync(users.Select(u => new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = meId,
            AddresseeId = u.Id,
            Status = FriendshipStatus.Accepted,
            AcceptedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        }).ToList());

        var svc = CreateService();

        var result = await svc.GetMutualFriendsAsync(meId, otherId);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetOtherUserFriendsAsync_WhenPrivate_ThrowsForbidden()
    {
        var meId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var other = BuildUser(otherId, "private-guy");
        other.Profile!.IsFriendListPublic = false;
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(otherId)).ReturnsAsync(other);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetOtherUserFriendsAsync(meId, otherId));
    }

    [Fact]
    public async Task GetOtherUserFriendsAsync_WhenQueryingSelf_ThrowsBadRequest()
    {
        var meId = Guid.NewGuid();

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() => svc.GetOtherUserFriendsAsync(meId, meId));
    }

    [Fact]
    public async Task UpdatePrivacyAsync_UpdatesProfile()
    {
        var meId = Guid.NewGuid();
        var me = BuildUser(meId);
        _userRepo.Setup(r => r.GetByIdWithProfileAsync(meId)).ReturnsAsync(me);

        var svc = CreateService();

        await svc.UpdatePrivacyAsync(meId, new UpdateFriendPrivacyDto
        {
            IsFriendListPublic = false,
            AcceptFriendRequestsFrom = "FriendsOfFriends",
            FriendLimit = 100
        });

        Assert.False(me.Profile!.IsFriendListPublic);
        Assert.Equal("FriendsOfFriends", me.Profile!.AcceptFriendRequestsFrom);
        Assert.Equal(100, me.Profile!.FriendLimit);
        _userRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetFriendSuggestionsAsync_ReturnsNonEmptyList()
    {
        var meId = Guid.NewGuid();
        var suggestedId = Guid.NewGuid();
        _friendshipRepo.Setup(r => r.GetFriendUserIdsAsync(meId)).ReturnsAsync(new List<Guid>());
        _lobbyMemberRepo.Setup(r => r.GetRecentMemberUserIdsAsync(meId, 30, 50)).ReturnsAsync(new List<Guid> { suggestedId });
        _userRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>())).ReturnsAsync(new List<User> { BuildUser(suggestedId, "suggested") });

        var svc = CreateService();

        var result = await svc.GetFriendSuggestionsAsync(meId);

        Assert.NotEmpty(result);
        Assert.Equal(suggestedId, result[0].UserId);
    }

    [Fact]
    public async Task MarkRequestAsReadAsync_WhenNotAddressee_ThrowsForbidden()
    {
        var meId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.MarkRequestAsReadAsync(meId, f.Id));
    }

    [Fact]
    public async Task MarkRequestAsReadAsync_WhenValid_SetsAddresseeReadAt()
    {
        var meId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var f = BuildFriendship(Guid.NewGuid(), requesterId, meId);
        _friendshipRepo.Setup(r => r.GetByIdAsync(f.Id)).ReturnsAsync(f);

        var svc = CreateService();

        await svc.MarkRequestAsReadAsync(meId, f.Id);

        Assert.NotNull(f.AddresseeReadAt);
        _friendshipRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region ExpireOldPendingRequestsAsync

    [Fact]
    public async Task ExpireOldPendingRequestsAsync_WhenNoneExpired_ReturnsZero()
    {
        _friendshipRepo.Setup(r => r.GetExpiredPendingAsync(It.IsAny<DateTime>())).ReturnsAsync(new List<Friendship>());

        var svc = CreateService();

        var count = await svc.ExpireOldPendingRequestsAsync(30);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ExpireOldPendingRequestsAsync_ExpiresAndSaves()
    {
        var f = BuildFriendship(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FriendshipStatus.Pending, DateTime.UtcNow.AddDays(-45));
        _friendshipRepo.Setup(r => r.GetExpiredPendingAsync(It.IsAny<DateTime>())).ReturnsAsync(new List<Friendship> { f });

        var svc = CreateService();

        var count = await svc.ExpireOldPendingRequestsAsync(30);

        Assert.Equal(1, count);
        Assert.Equal(FriendshipStatus.Removed, f.Status);
        _friendshipRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ExpireOldPendingRequestsAsync_NegativeDays_FallsBackTo30()
    {
        _friendshipRepo.Setup(r => r.GetExpiredPendingAsync(It.IsAny<DateTime>())).ReturnsAsync(new List<Friendship>());

        var svc = CreateService();

        var count = await svc.ExpireOldPendingRequestsAsync(-5);

        Assert.Equal(0, count);
        _friendshipRepo.Verify(r => r.GetExpiredPendingAsync(It.Is<DateTime>(d => (DateTime.UtcNow - d).TotalDays >= 29)), Times.Once);
    }

    #endregion
}