using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class LobbyServiceTests
{
    [Fact]
    public async Task CreateLobbyAsync_WithValidRequest_ReturnsLobbyResponse()
    {
        var hostId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync(new GameTemplate
        {
            Id = gameId,
            Name = "Catan",
            IsActive = true
        });

        var lobbyRepo = new Mock<ILobbyRepository>();

        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new CreateLobbyRequestDto
        {
            GameTemplateId = gameId,
            ScheduledStartTime = DateTime.UtcNow.AddHours(2),
            MaxMembers = 4
        };

        var result = await service.CreateLobbyAsync(hostId, request);

        Assert.Equal(LobbyStatus.Open, result.Status);
        Assert.Equal(hostId, result.HostUserId);
        Assert.Single(result.Members);
        Assert.True(result.Members[0].IsHost);
    }

    [Fact]
    public async Task JoinLobbyAsync_WithFullLobby_ThrowsConflictException()
    {
        var lobbyId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Full,
            MaxMembers = 2,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsActive = true },
                new LobbyMember { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsActive = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();

        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<ConflictException>(() => service.JoinLobbyAsync(lobbyId, Guid.NewGuid()));
    }

    #region CreateLobbyAsync

    [Fact]
    public async Task CreateLobbyAsync_ScheduledTimeTooSoon_ThrowsBadRequestException()
    {
        var hostId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var gameRepo = new Mock<IGameTemplateRepository>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new CreateLobbyRequestDto
        {
            GameTemplateId = gameId,
            ScheduledStartTime = DateTime.UtcNow.AddMinutes(2), // less than 5 min away
            MaxMembers = 4
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => service.CreateLobbyAsync(hostId, request));

        Assert.Contains("5 phút", ex.Message);
    }

    [Fact]
    public async Task CreateLobbyAsync_MaxMembersLessThanTwo_ThrowsBadRequestException()
    {
        var hostId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync(new GameTemplate
        {
            Id = gameId,
            Name = "Catan",
            MinPlayers = 2,
            MaxPlayers = 8,
            IsActive = true
        });
        var lobbyRepo = new Mock<ILobbyRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new CreateLobbyRequestDto
        {
            GameTemplateId = gameId,
            ScheduledStartTime = DateTime.UtcNow.AddHours(2),
            MaxMembers = 1
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => service.CreateLobbyAsync(hostId, request));

        Assert.Contains("Số người tối đa", ex.Message);
    }

    [Fact]
    public async Task CreateLobbyAsync_GameNotFound_ThrowsNotFoundException()
    {
        var hostId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync((GameTemplate?)null);

        var lobbyRepo = new Mock<ILobbyRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new CreateLobbyRequestDto
        {
            GameTemplateId = gameId,
            ScheduledStartTime = DateTime.UtcNow.AddHours(2),
            MaxMembers = 4
        };

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.CreateLobbyAsync(hostId, request));
    }

    [Fact]
    public async Task CreateLobbyAsync_ValidRequest_SetsHostAsFirstMember()
    {
        var hostId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync(new GameTemplate
        {
            Id = gameId,
            Name = "Catan",
            IsActive = true
        });

        var lobbyRepo = new Mock<ILobbyRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new CreateLobbyRequestDto
        {
            GameTemplateId = gameId,
            ScheduledStartTime = DateTime.UtcNow.AddHours(2),
            MaxMembers = 4,
            CancellationLeadTimeMinutes = 15
        };

        var result = await service.CreateLobbyAsync(hostId, request);

        Assert.Equal(LobbyStatus.Open, result.Status);
        Assert.Equal(hostId, result.HostUserId);
        Assert.Single(result.Members);
        Assert.True(result.Members[0].IsHost);
        lobbyRepo.Verify(r => r.AddAsync(It.IsAny<Lobby>()), Times.Once);
        lobbyRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region JoinLobbyAsync

    [Fact]
    public async Task JoinLobbyAsync_LobbyNotFound_ThrowsNotFoundException()
    {
        var lobbyId = Guid.NewGuid();

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync((Lobby?)null);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.JoinLobbyAsync(lobbyId, Guid.NewGuid()));
    }

    [Fact]
    public async Task JoinLobbyAsync_LobbyNotOpen_ThrowsConflictException()
    {
        var lobbyId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Full,
            MaxMembers = 4,
            Members = new List<LobbyMember>()
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => service.JoinLobbyAsync(lobbyId, Guid.NewGuid()));
    }

    [Fact]
    public async Task JoinLobbyAsync_AlreadyMember_ThrowsConflictException()
    {
        var lobbyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = userId, IsActive = true, IsHost = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => service.JoinLobbyAsync(lobbyId, userId));
    }

    [Fact]
    public async Task JoinLobbyAsync_SeatCountExceeded_ThrowsConflictException()
    {
        var lobbyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            SeatCount = 2,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsActive = true, IsHost = true },
                new LobbyMember { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsActive = true } // already at SeatCount limit
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => service.JoinLobbyAsync(lobbyId, userId));
    }

    [Fact]
    public async Task JoinLobbyAsync_ValidRequest_AddsMember()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsActive = true, IsHost = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);
        lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Lobby>());
        lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Lobby>());

        var userRepo = new Mock<IUserManagementRepository>();
        userRepo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>())).ReturnsAsync(new User { Id = newUserId, Username = "newuser", Email = "new@test.com" });

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, userRepo.Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.JoinLobbyAsync(lobbyId, newUserId);

        Assert.Equal(2, result.Members.Count);
        lobbyRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task JoinLobbyAsync_FillsToMaxMembers_TransitionsToFull()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var user1Id = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            MaxMembers = 2,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsActive = true, IsHost = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);
        lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Lobby>());
        lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Lobby>());

        var userRepo = new Mock<IUserManagementRepository>();
        userRepo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>())).ReturnsAsync(new User { Id = user1Id, Username = "user1", Email = "u1@test.com" });

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, userRepo.Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.JoinLobbyAsync(lobbyId, user1Id);

        Assert.Equal(LobbyStatus.Full, result.Status);
        Assert.Equal(2, result.Members.Count);
    }

    #endregion

    #region LeaveLobbyAsync

    [Fact]
    public async Task LeaveLobbyAsync_LobbyNotFound_ThrowsNotFoundException()
    {
        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Lobby?)null);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.LeaveLobbyAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task LeaveLobbyAsync_NotAMember_ThrowsNotFoundException()
    {
        var lobbyId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            Members = new List<LobbyMember>()
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.LeaveLobbyAsync(lobbyId, Guid.NewGuid()));
    }

    [Fact]
    public async Task LeaveLobbyAsync_HostLeaves_SetsStatusToHostCancelled()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsActive = true, IsHost = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.LeaveLobbyAsync(lobbyId, hostId);

        Assert.Equal(LobbyStatus.HostCancelled, result.Status);
        // Note: Host is filtered out from result.Members because MapLobbyDto only returns IsActive members
        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task LeaveLobbyAsync_NonHostLeaves_DoesNotCancelLobby()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsActive = true, IsHost = true },
                new LobbyMember { Id = Guid.NewGuid(), UserId = memberId, IsActive = true, IsHost = false }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.LeaveLobbyAsync(lobbyId, memberId);

        Assert.Equal(LobbyStatus.Open, result.Status);
        Assert.Single(result.Members);
    }

    #endregion

    #region LockLobbyAsync

    [Fact]
    public async Task LockLobbyAsync_OnlyHostCanLock_ThrowsForbiddenException()
    {
        var lobbyId = Guid.NewGuid();
        var nonHostId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsActive = true, IsHost = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.LockLobbyAsync(lobbyId, nonHostId));
    }

    [Fact]
    public async Task LockLobbyAsync_LobbyNotOpen_ThrowsConflictException()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Full,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsActive = true, IsHost = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => service.LockLobbyAsync(lobbyId, hostId));
    }

    [Fact]
    public async Task LockLobbyAsync_ValidRequest_TransitionsToFull()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsActive = true, IsHost = true },
                new LobbyMember { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsActive = true, IsHost = false }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.LockLobbyAsync(lobbyId, hostId);

        Assert.Equal(LobbyStatus.Full, result.Status);
        lobbyRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region SearchLobbiesAsync

    [Fact]
    public async Task SearchLobbiesAsync_NoKarmaFilter_ReturnsAllActiveLobbies()
    {
        var gameId = Guid.NewGuid();
        var lobbies = new List<Lobby>
        {
            new Lobby { Id = Guid.NewGuid(), Status = LobbyStatus.Open, MaxMembers = 4, Members = new List<LobbyMember>() },
            new Lobby { Id = Guid.NewGuid(), Status = LobbyStatus.Open, MaxMembers = 3, Members = new List<LobbyMember>() }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetActiveLobbiesForGameAsync(gameId, null)).ReturnsAsync(lobbies);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new SearchLobbiesRequestDto { GameTemplateId = gameId };
        var result = await service.SearchLobbiesAsync(request);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SearchLobbiesAsync_WithKarmaFilter_FiltersCorrectly()
    {
        var gameId = Guid.NewGuid();
        var highKarmaUser = new User { Id = Guid.NewGuid(), Username = "highkarma", Email = "high@test.com", Profile = new UserProfile { KarmaPoints = 95 } };
        var lowKarmaUser = new User { Id = Guid.NewGuid(), Username = "lowkarma", Email = "low@test.com", Profile = new UserProfile { KarmaPoints = 50 } };

        var lobbies = new List<Lobby>
        {
            new Lobby
            {
                Id = Guid.NewGuid(),
                Status = LobbyStatus.Open,
                MaxMembers = 4,
                Members = new List<LobbyMember>
                {
                    new LobbyMember { Id = Guid.NewGuid(), UserId = highKarmaUser.Id, IsActive = true, User = highKarmaUser }
                }
            },
            new Lobby
            {
                Id = Guid.NewGuid(),
                Status = LobbyStatus.Open,
                MaxMembers = 3,
                Members = new List<LobbyMember>
                {
                    new LobbyMember { Id = Guid.NewGuid(), UserId = lowKarmaUser.Id, IsActive = true, User = lowKarmaUser }
                }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetActiveLobbiesForGameAsync(gameId, null)).ReturnsAsync(lobbies);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new SearchLobbiesRequestDto { GameTemplateId = gameId, MinKarmaScore = 80 };
        var result = await service.SearchLobbiesAsync(request);

        Assert.Single(result);
    }

    #endregion

    #region CloseLobbyAsync

    [Fact]
    public async Task CloseLobbyAsync_OnlyHostCanClose_ThrowsForbiddenException()
    {
        var lobbyId = Guid.NewGuid();
        var nonHostId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsActive = true, IsHost = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.CloseLobbyAsync(lobbyId, nonHostId, null));
    }

    [Fact]
    public async Task CloseLobbyAsync_ValidRequest_TransitionsToClosed()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsActive = true, IsHost = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.CloseLobbyAsync(lobbyId, hostId, null);

        Assert.Equal(LobbyStatus.Closed, result.Status);
    }

    #endregion

    #region OpenKarmaWindowAsync

    [Fact]
    public async Task OpenKarmaWindowAsync_OnlyHostCanOpen_ThrowsForbiddenException()
    {
        var lobbyId = Guid.NewGuid();
        var nonHostId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = Guid.NewGuid(),
            Status = LobbyStatus.Closed,
            Members = new List<LobbyMember>()
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.OpenKarmaWindowAsync(lobbyId, nonHostId));
    }

    [Fact]
    public async Task OpenKarmaWindowAsync_ValidRequest_SetsRatingOpenedAt()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = LobbyStatus.Closed,
            Members = new List<LobbyMember>()
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.OpenKarmaWindowAsync(lobbyId, hostId);

        lobbyRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region MinKarmaScore (BR-10)

    [Fact]
    public async Task CreateLobbyAsync_WithMinKarmaScore_PersistsOnEntity()
    {
        var hostId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync(new GameTemplate
        {
            Id = gameId,
            Name = "Catan",
            IsActive = true
        });

        var lobbyRepo = new Mock<ILobbyRepository>();
        Lobby? captured = null;
        lobbyRepo.Setup(r => r.AddAsync(It.IsAny<Lobby>()))
            .Callback<Lobby>(l => captured = l)
            .Returns(Task.CompletedTask);

        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new CreateLobbyRequestDto
        {
            GameTemplateId = gameId,
            ScheduledStartTime = DateTime.UtcNow.AddHours(2),
            MaxMembers = 4,
            MinKarmaScore = 80
        };

        var result = await service.CreateLobbyAsync(hostId, request);

        Assert.NotNull(captured);
        Assert.Equal(80, captured!.MinKarmaScore);
        Assert.Equal(80, result.MinKarmaScore);
    }

    [Fact]
    public async Task CreateLobbyAsync_WithoutMinKarmaScore_PersistsNull()
    {
        var hostId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetByIdWithComponentsAsync(gameId)).ReturnsAsync(new GameTemplate
        {
            Id = gameId,
            Name = "Catan",
            IsActive = true
        });

        var lobbyRepo = new Mock<ILobbyRepository>();
        Lobby? captured = null;
        lobbyRepo.Setup(r => r.AddAsync(It.IsAny<Lobby>()))
            .Callback<Lobby>(l => captured = l)
            .Returns(Task.CompletedTask);

        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new CreateLobbyRequestDto
        {
            GameTemplateId = gameId,
            ScheduledStartTime = DateTime.UtcNow.AddHours(2),
            MaxMembers = 4
        };

        var result = await service.CreateLobbyAsync(hostId, request);

        Assert.NotNull(captured);
        Assert.Null(captured!.MinKarmaScore);
        Assert.Null(result.MinKarmaScore);
    }

    [Fact]
    public async Task UpdateLobbyAsync_SetMinKarmaScore_PersistsOnEntity()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = LobbyStatus.Open,
            MaxMembers = 5,
            MinPlayers = 2,
            MinKarmaScore = null,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsHost = true, IsActive = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new UpdateLobbyRequestDto { MinKarmaScore = 85 };

        var result = await service.UpdateLobbyAsync(lobbyId, hostId, request);

        Assert.Equal(85, lobby.MinKarmaScore);
        Assert.Equal(85, result.MinKarmaScore);
        lobbyRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateLobbyAsync_ChangeMinKarmaScore_UpdatesExistingValue()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = LobbyStatus.Open,
            MaxMembers = 5,
            MinPlayers = 2,
            MinKarmaScore = 50,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsHost = true, IsActive = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new UpdateLobbyRequestDto { MinKarmaScore = 95 };

        await service.UpdateLobbyAsync(lobbyId, hostId, request);

        Assert.Equal(95, lobby.MinKarmaScore);
    }

    [Fact]
    public async Task UpdateLobbyAsync_OmitMinKarmaScore_KeepsExistingValue()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = LobbyStatus.Open,
            MaxMembers = 5,
            MinPlayers = 2,
            MinKarmaScore = 75,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsHost = true, IsActive = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        // Chỉ update Description, không đụng MinKarmaScore.
        var request = new UpdateLobbyRequestDto { Description = "Updated text" };

        await service.UpdateLobbyAsync(lobbyId, hostId, request);

        Assert.Equal(75, lobby.MinKarmaScore); // Không đổi
        Assert.Equal("Updated text", lobby.Description);
    }

    [Theory]
    [InlineData(LobbyStatus.InProgress)]
    [InlineData(LobbyStatus.Closed)]
    [InlineData(LobbyStatus.HostCancelled)]
    [InlineData(LobbyStatus.TimeoutFailed)]
    public async Task UpdateLobbyAsync_TerminalStatus_RejectsMinKarmaScoreChange(LobbyStatus status)
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = status,
            MaxMembers = 5,
            MinPlayers = 2,
            MinKarmaScore = 50,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsHost = true, IsActive = true }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var request = new UpdateLobbyRequestDto { MinKarmaScore = 95 };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateLobbyAsync(lobbyId, hostId, request));

        Assert.Equal(50, lobby.MinKarmaScore); // Không đổi
    }

    [Fact]
    public async Task JoinLobbyAsync_MemberKarmaBelowRequirement_ThrowsForbidden()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            MinKarmaScore = 80,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsHost = true, IsActive = true, Status = LobbyMemberStatus.Joined }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);

        var userRepo = new Mock<IUserManagementRepository>();
        userRepo.Setup(r => r.GetByIdWithProfileAsync(newUserId)).ReturnsAsync(new User
        {
            Id = newUserId,
            Username = "newbie",
            Email = "newbie@test.com",
            Profile = new UserProfile { KarmaPoints = 60 } // dưới 80
        });

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, userRepo.Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => service.JoinLobbyAsync(lobbyId, newUserId));

        Assert.Contains("80", ex.Message);
        Assert.Contains("60", ex.Message);
    }

    [Fact]
    public async Task JoinLobbyAsync_MemberKarmaMeetsRequirement_Succeeds()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            IsPrivate = false, // public → skip invite check
            MinKarmaScore = 80,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsHost = true, IsActive = true, Status = LobbyMemberStatus.Joined }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);
        lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Lobby>());
        lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Lobby>());

        var userRepo = new Mock<IUserManagementRepository>();
        userRepo.Setup(r => r.GetByIdWithProfileAsync(newUserId)).ReturnsAsync(new User
        {
            Id = newUserId,
            Username = "veteran",
            Email = "veteran@test.com",
            Profile = new UserProfile { KarmaPoints = 80 } // exactly meet
        });

        var gameRepo = new Mock<IGameTemplateRepository>();
        var hubService = new Mock<ILobbyHubService>();
        var messageService = new Mock<ILobbyMessageService>();

        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, userRepo.Object, new Mock<ILobbyInviteRepository>().Object, hubService.Object, messageService.Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.JoinLobbyAsync(lobbyId, newUserId);

        Assert.Equal(2, result.Members.Count);
        Assert.True(result.Members.Any(m => m.UserId == newUserId));
    }

    [Fact]
    public async Task JoinLobbyAsync_NoMinKarmaScore_NoCheck()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            IsPrivate = false, // public → skip invite check
            MinKarmaScore = null, // No requirement
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsHost = true, IsActive = true, Status = LobbyMemberStatus.Joined }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        // JoinLobbyAsync uses GetByIdForUpdateAsync (H4: SELECT FOR UPDATE) to prevent race conditions.
        lobbyRepo.Setup(r => r.GetByIdForUpdateAsync(lobbyId)).ReturnsAsync(lobby);
        lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Lobby>());
        lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Lobby>());

        var userRepo = new Mock<IUserManagementRepository>();
        // KHÔNG cần setup GetByIdWithProfileAsync vì MinKarmaScore=null → skip check.
        userRepo.Setup(r => r.GetByIdWithProfileAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new User { Id = newUserId, Username = "any", Email = "any@test.com", Profile = new UserProfile { KarmaPoints = 0 } });

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, userRepo.Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.JoinLobbyAsync(lobbyId, newUserId);

        Assert.Equal(2, result.Members.Count);
    }

    [Fact]
    public async Task LobbyResponseDto_IncludesMinKarmaScore()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            MinKarmaScore = 70,
            Members = new List<LobbyMember>
            {
                new LobbyMember { Id = Guid.NewGuid(), UserId = hostId, IsHost = true, IsActive = true, Status = LobbyMemberStatus.Joined }
            }
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var gameRepo = new Mock<IGameTemplateRepository>();
        var service = new LobbyService(lobbyRepo.Object, gameRepo.Object, new Mock<IUserManagementRepository>().Object, new Mock<ILobbyInviteRepository>().Object, new Mock<ILobbyHubService>().Object, new Mock<ILobbyMessageService>().Object, new Mock<ILobbyMessageRepository>().Object, new Mock<IFriendshipRepository>().Object, new Mock<IReservationRepository>().Object, new Mock<IWalletService>().Object, new Mock<ISeatInventoryRepository>().Object, new Mock<IGameInventoryRepository>().Object, new Mock<IOutboxRepository>().Object, new Mock<ICafeRepository>().Object, new Mock<BoardVerse.Data.BoardVerseDbContext>(new DbContextOptions<BoardVerse.Data.BoardVerseDbContext>()).Object, new EligibilityValidator(), new Mock<IUserProfileService>().Object, new Mock<Microsoft.Extensions.Logging.ILogger<LobbyService>>().Object);

        var result = await service.GetLobbyAsync(lobbyId);

        Assert.NotNull(result);
        Assert.Equal(70, result.MinKarmaScore);
    }

    #endregion
}

