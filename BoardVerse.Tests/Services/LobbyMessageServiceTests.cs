using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho LobbyMessageService — BR-LOBBY-*: gửi tin nhắn, system message, lấy history.
/// </summary>
public class LobbyMessageServiceTests
{
    private readonly Mock<ILobbyMessageRepository> _msgRepo = new();
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<ILobbyHubService> _hub = new();
    private readonly Mock<IUserManagementRepository> _userRepo = new();

    private LobbyMessageService CreateService() => new(
        _msgRepo.Object,
        _lobbyRepo.Object,
        _hub.Object,
        _userRepo.Object);

    private static Lobby BuildLobby(Guid id, params LobbyMember[] members) => new()
    {
        Id = id,
        Status = LobbyStatus.Open,
        MaxMembers = 4,
        Members = members.ToList()
    };

    private static LobbyMember BuildMember(Guid userId, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        IsHost = false,
        IsActive = isActive
    };

    private static User BuildUser(Guid userId, string username = "tester") => new()
    {
        Id = userId,
        Username = username,
        Email = $"{username}@test.local"
    };

    #region SendMessageAsync

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendMessageAsync_EmptyContent_ThrowsBadRequest(string? content)
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.SendMessageAsync(Guid.NewGuid(), Guid.NewGuid(), content!));
    }

    [Fact]
    public async Task SendMessageAsync_ContentTooLong_ThrowsBadRequest()
    {
        var sut = CreateService();
        var longContent = new string('x', 1001);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.SendMessageAsync(Guid.NewGuid(), Guid.NewGuid(), longContent));
    }

    [Fact]
    public async Task SendMessageAsync_LobbyNotFound_ThrowsNotFound()
    {
        var lobbyId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Lobby?)null);

        var sut = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.SendMessageAsync(lobbyId, senderId, "hello"));
    }

    [Fact]
    public async Task SendMessageAsync_SenderNotActiveMember_ThrowsForbidden()
    {
        var lobbyId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(lobbyId)); // no members

        var sut = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.SendMessageAsync(lobbyId, senderId, "hello"));
    }

    [Fact]
    public async Task SendMessageAsync_InactiveMember_ThrowsForbidden()
    {
        var lobbyId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(lobbyId, BuildMember(senderId, isActive: false)));

        var sut = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.SendMessageAsync(lobbyId, senderId, "hello"));
    }

    [Fact]
    public async Task SendMessageAsync_ValidRequest_PersistsAndNotifiesHub()
    {
        var lobbyId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(lobbyId, BuildMember(senderId)));
        _userRepo.Setup(r => r.GetByIdAsync(senderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(senderId, "alice"));

        var sut = CreateService();
        var dto = await sut.SendMessageAsync(lobbyId, senderId, "  hello world  ");

        Assert.Equal(lobbyId, dto.LobbyId);
        Assert.Equal(senderId, dto.SenderId);
        Assert.Equal("alice", dto.SenderName);
        Assert.Equal("hello world", dto.Content);
        Assert.False(dto.IsSystem);

        _msgRepo.Verify(r => r.AddAsync(It.Is<LobbyMessage>(m =>
            m.LobbyId == lobbyId &&
            m.SenderId == senderId &&
            m.Content == "hello world" &&
            !m.IsSystem), It.IsAny<CancellationToken>()), Times.Once);
        _msgRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _hub.Verify(h => h.NotifyMessagePosted(lobbyId, It.Is<LobbyMessageDto>(d =>
            d.Content == "hello world" && d.IsSystem == false)), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_SenderNotInUserRepo_DefaultsUsernameToEmpty()
    {
        var lobbyId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(lobbyId, BuildMember(senderId)));
        _userRepo.Setup(r => r.GetByIdAsync(senderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var sut = CreateService();
        var dto = await sut.SendMessageAsync(lobbyId, senderId, "hi");

        Assert.Equal(string.Empty, dto.SenderName);
    }

    [Fact]
    public async Task SendMessageAsync_MessageMaxLength1000_Accepted()
    {
        var lobbyId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(lobbyId, BuildMember(senderId)));
        _userRepo.Setup(r => r.GetByIdAsync(senderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(senderId));

        var sut = CreateService();
        var content = new string('a', 1000);

        var dto = await sut.SendMessageAsync(lobbyId, senderId, content);

        Assert.Equal(content, dto.Content);
    }

    #endregion

    #region GetMessagesAsync

    [Fact]
    public async Task GetMessagesAsync_NoMessages_ReturnsEmptyList()
    {
        var lobbyId = Guid.NewGuid();
        _msgRepo.Setup(r => r.GetByLobbyAsync(lobbyId, null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMessage>());

        var sut = CreateService();
        var result = await sut.GetMessagesAsync(lobbyId, null);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMessagesAsync_MapsSystemMessageSenderNameToSystem()
    {
        var lobbyId = Guid.NewGuid();
        var msg = new LobbyMessage
        {
            Id = Guid.NewGuid(),
            LobbyId = lobbyId,
            SenderId = null,
            Content = "Lobby closed",
            IsSystem = true,
            CreatedAt = DateTime.UtcNow
        };
        _msgRepo.Setup(r => r.GetByLobbyAsync(lobbyId, null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMessage> { msg });

        var sut = CreateService();
        var result = await sut.GetMessagesAsync(lobbyId, null);

        Assert.Single(result);
        Assert.Equal("Hệ thống", result[0].SenderName);
        Assert.True(result[0].IsSystem);
    }

    [Fact]
    public async Task GetMessagesAsync_MapsUserMessageWithUnknownSenderFallback()
    {
        var lobbyId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var msg = new LobbyMessage
        {
            Id = Guid.NewGuid(),
            LobbyId = lobbyId,
            SenderId = senderId,
            Sender = null, // not loaded
            Content = "hi",
            IsSystem = false,
            CreatedAt = DateTime.UtcNow
        };
        _msgRepo.Setup(r => r.GetByLobbyAsync(lobbyId, null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMessage> { msg });

        var sut = CreateService();
        var result = await sut.GetMessagesAsync(lobbyId, null);

        Assert.Equal("Unknown", result[0].SenderName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(500)] // above 200 cap → default 50
    public async Task GetMessagesAsync_LimitOutOfRange_DefaultsTo50(int requested)
    {
        var lobbyId = Guid.NewGuid();
        _msgRepo.Setup(r => r.GetByLobbyAsync(lobbyId, null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMessage>());

        var sut = CreateService();
        await sut.GetMessagesAsync(lobbyId, null, limit: requested);

        _msgRepo.Verify(r => r.GetByLobbyAsync(lobbyId, null, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_PassesBeforeCursorToRepository()
    {
        var lobbyId = Guid.NewGuid();
        var cursor = DateTime.UtcNow.AddMinutes(-10);
        _msgRepo.Setup(r => r.GetByLobbyAsync(lobbyId, cursor, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMessage>());

        var sut = CreateService();
        await sut.GetMessagesAsync(lobbyId, cursor);

        _msgRepo.Verify(r => r.GetByLobbyAsync(lobbyId, cursor, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AddSystemMessageAsync

    [Fact]
    public async Task AddSystemMessageAsync_PersistsSystemMessageAndNotifies()
    {
        var lobbyId = Guid.NewGuid();
        var sut = CreateService();

        await sut.AddSystemMessageAsync(lobbyId, "Lobby closed");

        _msgRepo.Verify(r => r.AddAsync(It.Is<LobbyMessage>(m =>
            m.LobbyId == lobbyId &&
            m.SenderId == null &&
            m.IsSystem &&
            m.Content == "Lobby closed"), It.IsAny<CancellationToken>()), Times.Once);
        _msgRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _hub.Verify(h => h.NotifyMessagePosted(lobbyId, It.Is<LobbyMessageDto>(d =>
            d.SenderName == "Hệ thống" && d.IsSystem)), Times.Once);
    }

    #endregion

    #region AddMemberJoinedMessageAsync

    [Fact]
    public async Task AddMemberJoinedMessageAsync_UserExists_UsesUsername()
    {
        var lobbyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(userId, "alice"));

        var sut = CreateService();
        await sut.AddMemberJoinedMessageAsync(lobbyId, userId);

        _msgRepo.Verify(r => r.AddAsync(It.Is<LobbyMessage>(m =>
            m.IsSystem && m.Content == "alice đã tham gia phòng chờ."), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddMemberJoinedMessageAsync_UserMissing_FallbackText()
    {
        var lobbyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var sut = CreateService();
        await sut.AddMemberJoinedMessageAsync(lobbyId, userId);

        _msgRepo.Verify(r => r.AddAsync(It.Is<LobbyMessage>(m =>
            m.IsSystem && m.Content!.Contains("Một người dùng")), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
