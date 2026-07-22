using BoardVerse.Core.DTOs.LobbyInvite;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class LobbyInviteServiceTests
{
    private readonly Mock<ILobbyInviteRepository> _inviteRepo = new();
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<ILobbyService> _lobbyService = new();
    private readonly Mock<IFriendshipRepository> _friendshipRepo = new();

    private LobbyInviteService CreateService() => new(
        _inviteRepo.Object,
        _lobbyRepo.Object,
        _lobbyService.Object,
        _friendshipRepo.Object);

    private static Lobby BuildLobby(Guid id, LobbyStatus status = LobbyStatus.Open, bool isPrivate = false, int maxMembers = 4)
    {
        return new Lobby
        {
            Id = id,
            Status = status,
            IsPrivate = isPrivate,
            MaxMembers = maxMembers,
            ShareCode = "ABC123",
            Description = "Catan lobby",
            GameTemplateId = Guid.NewGuid(),
            ScheduledStartTime = DateTime.UtcNow.AddHours(2),
            Members = new List<LobbyMember>()
        };
    }

    private static LobbyMember BuildMember(Guid userId, bool isHost = false, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        IsHost = isHost,
        IsActive = active
    };

    #region SendInviteAsync

    [Fact]
    public async Task SendInviteAsync_WhenInviteeIsSelf_ThrowsBadRequest()
    {
        var lobbyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.SendInviteAsync(lobbyId, userId, new SendLobbyInviteRequestDto { InviteeId = userId }));
    }

    [Fact]
    public async Task SendInviteAsync_WhenLobbyNotFound_ThrowsNotFound()
    {
        var lobbyId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync((Lobby?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.SendInviteAsync(lobbyId, Guid.NewGuid(), new SendLobbyInviteRequestDto { InviteeId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task SendInviteAsync_WhenInviterNotMember_ThrowsForbidden()
    {
        var lobbyId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId);
        lobby.Members.Add(BuildMember(Guid.NewGuid(), isHost: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SendInviteAsync(lobbyId, Guid.NewGuid(), new SendLobbyInviteRequestDto { InviteeId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task SendInviteAsync_WhenLobbyClosed_ThrowsConflict()
    {
        var lobbyId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId, status: LobbyStatus.Closed);
        lobby.Members.Add(BuildMember(inviterId, isHost: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.SendInviteAsync(lobbyId, inviterId, new SendLobbyInviteRequestDto { InviteeId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task SendInviteAsync_WhenInviteeAlreadyMember_ThrowsConflict()
    {
        var lobbyId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId);
        lobby.Members.Add(BuildMember(inviterId, isHost: true));
        lobby.Members.Add(BuildMember(inviteeId, active: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.SendInviteAsync(lobbyId, inviterId, new SendLobbyInviteRequestDto { InviteeId = inviteeId }));
    }

    [Fact]
    public async Task SendInviteAsync_WhenBlockedByOtherParty_ThrowsForbidden()
    {
        var lobbyId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId);
        lobby.Members.Add(BuildMember(inviterId, isHost: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        _friendshipRepo.Setup(r => r.GetByPairAsync(inviterId, inviteeId))
            .ReturnsAsync(new Friendship { Id = Guid.NewGuid(), RequesterId = inviteeId, AddresseeId = inviterId, Status = FriendshipStatus.Blocked });

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SendInviteAsync(lobbyId, inviterId, new SendLobbyInviteRequestDto { InviteeId = inviteeId }));
    }

    [Fact]
    public async Task SendInviteAsync_WhenPrivateLobbyAndNotFriend_ThrowsForbidden()
    {
        var lobbyId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId, isPrivate: true);
        lobby.Members.Add(BuildMember(inviterId, isHost: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        _friendshipRepo.Setup(r => r.GetByPairAsync(inviterId, inviteeId)).ReturnsAsync((Friendship?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SendInviteAsync(lobbyId, inviterId, new SendLobbyInviteRequestDto { InviteeId = inviteeId }));
    }

    [Fact]
    public async Task SendInviteAsync_WhenPendingInviteExists_ThrowsConflict()
    {
        var lobbyId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId);
        lobby.Members.Add(BuildMember(inviterId, isHost: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        _friendshipRepo.Setup(r => r.GetByPairAsync(inviterId, inviteeId)).ReturnsAsync((Friendship?)null);
        _inviteRepo.Setup(r => r.GetPendingInviteAsync(lobbyId, inviteeId))
            .ReturnsAsync(new LobbyInvite { Id = Guid.NewGuid() });

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.SendInviteAsync(lobbyId, inviterId, new SendLobbyInviteRequestDto { InviteeId = inviteeId }));
    }

    [Fact]
    public async Task SendInviteAsync_PublicLobbyNoFriendship_CreatesInvite()
    {
        var lobbyId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId);
        lobby.Members.Add(BuildMember(inviterId, isHost: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        _friendshipRepo.Setup(r => r.GetByPairAsync(inviterId, inviteeId)).ReturnsAsync((Friendship?)null);
        _inviteRepo.Setup(r => r.GetPendingInviteAsync(lobbyId, inviteeId)).ReturnsAsync((LobbyInvite?)null);

        LobbyInvite? captured = null;
        _inviteRepo.Setup(r => r.AddAsync(It.IsAny<LobbyInvite>()))
            .Callback<LobbyInvite>(i => captured = i)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        var result = await svc.SendInviteAsync(lobbyId, inviterId, new SendLobbyInviteRequestDto
        {
            InviteeId = inviteeId,
            Message = "Join Catan at 7pm"
        });

        Assert.NotNull(captured);
        Assert.Equal(inviterId, captured!.InviterId);
        Assert.Equal(inviteeId, captured.InviteeId);
        Assert.Equal(LobbyInviteStatus.Pending, captured.Status);
        Assert.Equal("Join Catan at 7pm", captured.Message);
        _inviteRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal(LobbyInviteStatus.Pending.ToString(), result.Status);
    }

    [Fact]
    public async Task SendInviteAsync_PrivateLobbyFriendship_Succeeds()
    {
        var lobbyId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId, isPrivate: true);
        lobby.Members.Add(BuildMember(inviterId, isHost: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        _friendshipRepo.Setup(r => r.GetByPairAsync(inviterId, inviteeId))
            .ReturnsAsync(new Friendship { Id = Guid.NewGuid(), RequesterId = inviterId, AddresseeId = inviteeId, Status = FriendshipStatus.Accepted });
        _inviteRepo.Setup(r => r.GetPendingInviteAsync(lobbyId, inviteeId)).ReturnsAsync((LobbyInvite?)null);
        _inviteRepo.Setup(r => r.AddAsync(It.IsAny<LobbyInvite>())).Returns(Task.CompletedTask);

        var svc = CreateService();

        var result = await svc.SendInviteAsync(lobbyId, inviterId, new SendLobbyInviteRequestDto { InviteeId = inviteeId });

        Assert.Equal(LobbyInviteStatus.Pending.ToString(), result.Status);
    }

    #endregion

    #region AcceptInviteAsync

    [Fact]
    public async Task AcceptInviteAsync_WhenInviteNotFound_ThrowsNotFound()
    {
        _inviteRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((LobbyInvite?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.AcceptInviteAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task AcceptInviteAsync_WhenNotRecipient_ThrowsForbidden()
    {
        var inviteId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = Guid.NewGuid(), InviterId = Guid.NewGuid(), InviteeId = Guid.NewGuid(), Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.AcceptInviteAsync(inviteId, Guid.NewGuid()));
    }

    [Fact]
    public async Task AcceptInviteAsync_WhenExpired_ThrowsConflict()
    {
        var inviteId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = Guid.NewGuid(), InviterId = Guid.NewGuid(), InviteeId = inviteeId, Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(-1) };
        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.AcceptInviteAsync(inviteId, inviteeId));
    }

    [Fact]
    public async Task AcceptInviteAsync_WhenLobbyClosed_ExpiresAndThrows()
    {
        var inviteId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = lobbyId, InviterId = Guid.NewGuid(), InviteeId = inviteeId, Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var lobby = BuildLobby(lobbyId, status: LobbyStatus.Closed);

        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.AcceptInviteAsync(inviteId, inviteeId));
        Assert.Equal(LobbyInviteStatus.Expired, invite.Status);
    }

    [Fact]
    public async Task AcceptInviteAsync_WhenLobbyFull_ExpiresAndThrows()
    {
        var inviteId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = lobbyId, InviterId = Guid.NewGuid(), InviteeId = inviteeId, Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var lobby = BuildLobby(lobbyId, maxMembers: 2);
        lobby.Members.Add(BuildMember(Guid.NewGuid()));
        lobby.Members.Add(BuildMember(Guid.NewGuid()));

        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.AcceptInviteAsync(inviteId, inviteeId));
        Assert.Equal(LobbyInviteStatus.Expired, invite.Status);
    }

    [Fact]
    public async Task AcceptInviteAsync_PrivateAndUnfriend_CancelsAndThrows()
    {
        var inviteId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = lobbyId, InviterId = inviterId, InviteeId = inviteeId, Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var lobby = BuildLobby(lobbyId, isPrivate: true);

        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        _friendshipRepo.Setup(r => r.GetByPairAsync(inviterId, inviteeId)).ReturnsAsync((Friendship?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.AcceptInviteAsync(inviteId, inviteeId));
        Assert.Equal(LobbyInviteStatus.Cancelled, invite.Status);
    }

    [Fact]
    public async Task AcceptInviteAsync_ValidRequest_JoinsAndAccepts()
    {
        var inviteId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = lobbyId, InviterId = inviterId, InviteeId = inviteeId, Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var lobby = BuildLobby(lobbyId);

        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        _lobbyService.Setup(s => s.JoinLobbyAsync(lobbyId, inviteeId)).ReturnsAsync(new Core.DTOs.Lobby.LobbyResponseDto { Id = lobbyId });

        var svc = CreateService();

        var result = await svc.AcceptInviteAsync(inviteId, inviteeId);

        Assert.Equal(LobbyInviteStatus.Accepted, invite.Status);
        Assert.NotNull(invite.RespondedAt);
        _lobbyService.Verify(s => s.JoinLobbyAsync(lobbyId, inviteeId), Times.Once);
        _inviteRepo.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
        Assert.Equal(LobbyInviteStatus.Accepted.ToString(), result.Status);
    }

    #endregion

    #region DeclineInviteAsync

    [Fact]
    public async Task DeclineInviteAsync_WhenNotRecipient_ThrowsForbidden()
    {
        var inviteId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = Guid.NewGuid(), InviterId = Guid.NewGuid(), InviteeId = Guid.NewGuid(), Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeclineInviteAsync(inviteId, Guid.NewGuid()));
    }

    [Fact]
    public async Task DeclineInviteAsync_WhenNotPending_ThrowsConflict()
    {
        var inviteId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = Guid.NewGuid(), InviterId = Guid.NewGuid(), InviteeId = inviteeId, Status = LobbyInviteStatus.Accepted, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.DeclineInviteAsync(inviteId, inviteeId));
    }

    [Fact]
    public async Task DeclineInviteAsync_ValidRequest_SetsDeclined()
    {
        var inviteId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = Guid.NewGuid(), InviterId = Guid.NewGuid(), InviteeId = inviteeId, Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);

        var svc = CreateService();

        await svc.DeclineInviteAsync(inviteId, inviteeId);

        Assert.Equal(LobbyInviteStatus.Declined, invite.Status);
        Assert.NotNull(invite.RespondedAt);
        _inviteRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region CancelInviteAsync

    [Fact]
    public async Task CancelInviteAsync_WhenNotInviter_ThrowsForbidden()
    {
        var inviteId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = Guid.NewGuid(), InviterId = Guid.NewGuid(), InviteeId = Guid.NewGuid(), Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CancelInviteAsync(inviteId, Guid.NewGuid()));
    }

    [Fact]
    public async Task CancelInviteAsync_WhenNotPending_ThrowsConflict()
    {
        var inviteId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = Guid.NewGuid(), InviterId = inviterId, InviteeId = Guid.NewGuid(), Status = LobbyInviteStatus.Declined, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.CancelInviteAsync(inviteId, inviterId));
    }

    [Fact]
    public async Task CancelInviteAsync_ValidRequest_SetsCancelled()
    {
        var inviteId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var invite = new LobbyInvite { Id = inviteId, LobbyId = Guid.NewGuid(), InviterId = inviterId, InviteeId = Guid.NewGuid(), Status = LobbyInviteStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        _inviteRepo.Setup(r => r.GetByIdAsync(inviteId)).ReturnsAsync(invite);

        var svc = CreateService();

        await svc.CancelInviteAsync(inviteId, inviterId);

        Assert.Equal(LobbyInviteStatus.Cancelled, invite.Status);
        Assert.NotNull(invite.RespondedAt);
        _inviteRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region Get / Share

    [Fact]
    public async Task GetMyPendingInvitesAsync_ReturnsMappedList()
    {
        var meId = Guid.NewGuid();
        var invites = new List<LobbyInvite>
        {
            new()
            {
                Id = Guid.NewGuid(),
                LobbyId = Guid.NewGuid(),
                InviterId = Guid.NewGuid(),
                InviteeId = meId,
                Status = LobbyInviteStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            }
        };
        _inviteRepo.Setup(r => r.GetPendingByInviteeAsync(meId)).ReturnsAsync(invites);

        var svc = CreateService();

        var result = await svc.GetMyPendingInvitesAsync(meId);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMyInvitesAsync_WithInvalidStatus_ThrowsBadRequest()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() => svc.GetMyInvitesAsync(Guid.NewGuid(), "NotAStatus"));
    }

    [Fact]
    public async Task GetMyInvitesAsync_WithNullStatus_ReturnsAll()
    {
        var meId = Guid.NewGuid();
        _inviteRepo.Setup(r => r.GetAllByInviteeAsync(meId, null)).ReturnsAsync(new List<LobbyInvite>());

        var svc = CreateService();

        var result = await svc.GetMyInvitesAsync(meId, null);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetShareInfoAsync_WhenNotMember_ThrowsForbidden()
    {
        var lobbyId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId);
        lobby.Members.Add(BuildMember(Guid.NewGuid(), isHost: true));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetShareInfoAsync(lobbyId, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetShareInfoAsync_WhenMember_ReturnsShareInfo()
    {
        var lobbyId = Guid.NewGuid();
        var meId = Guid.NewGuid();
        var lobby = BuildLobby(lobbyId);
        lobby.Members.Add(BuildMember(meId));
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var svc = CreateService();

        var result = await svc.GetShareInfoAsync(lobbyId, meId);

        Assert.Equal(lobbyId, result.LobbyId);
        Assert.Equal("ABC123", result.ShareCode);
        Assert.False(result.IsPrivate);
    }

    #endregion
}