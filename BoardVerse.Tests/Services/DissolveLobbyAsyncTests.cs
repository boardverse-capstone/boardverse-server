using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Regression tests cho <see cref="LobbyService.DissolveLobbyAsync"/> — fix các gap P0:
/// <list type="number">
///   <item><description>BR-REFUND-02/03: hoàn BVC theo grace 15p + mốc 24h/6h + ghi ledger (Gap #1).</description></item>
///   <item><description>BR-RESERVATION-01/02 + §XVII.4: giải phóng SeatInventory + GameInventory atomic (Gap #6).</description></item>
///   <item><description>BR §XVII.1: idempotency key ổn định cho replay.</description></item>
///   <item><description>Status guard: thêm Viable vào danh sách terminal (Gap #10).</description></item>
/// </list>
/// </summary>
public class DissolveLobbyAsyncTests
{
    private static LobbyService BuildService(
        Mock<ILobbyRepository> lobbyRepo,
        Mock<IReservationRepository> reservationRepo,
        Mock<IWalletService> walletService,
        Mock<ISeatInventoryRepository> seatRepo,
        Mock<IGameInventoryRepository> gameRepo)
    {
        return new LobbyService(
            lobbyRepo.Object,
            new Mock<IGameTemplateRepository>().Object,
            new Mock<IUserManagementRepository>().Object,
            new Mock<ILobbyInviteRepository>().Object,
            new Mock<ILobbyHubService>().Object,
            new Mock<ILobbyMessageService>().Object,
            new Mock<ILobbyMessageRepository>().Object,
            new Mock<IFriendshipRepository>().Object,
            reservationRepo.Object,
            walletService.Object,
            seatRepo.Object,
            gameRepo.Object,
            SetupOutboxRepository(),
            new Mock<ICafeRepository>().Object,
            new Mock<BoardVerseDbContext>(new DbContextOptions<BoardVerseDbContext>()).Object,
            new EligibilityValidator(),
            new Mock<IUserProfileService>().Object,
            new Mock<ILogger<LobbyService>>().Object);
    }

    /// <summary>Mock IOutboxRepository với AddAsync setup để tránh NRE trong unit test.</summary>
    private static IOutboxRepository SetupOutboxRepository()
    {
        var outboxRepo = new Mock<IOutboxRepository>();
        outboxRepo.Setup(r => r.AddAsync(It.IsAny<OutboxEvent>()))
            .Returns(Task.CompletedTask);
        return outboxRepo.Object;
    }

    /// <summary>
    /// BuildService variant cho Gap #2 test: cho phép verify <c>_outboxRepository.AddAsync(...)</c>.
    /// Set up <c>AddAsync</c> = It.IsAny&lt;OutboxEvent&gt; để capture Add calls.
    /// </summary>
    private static (LobbyService Service, Mock<IOutboxRepository> OutboxRepo) BuildServiceWithOutboxCapture(
        Mock<ILobbyRepository> lobbyRepo,
        Mock<IReservationRepository> reservationRepo,
        Mock<IWalletService> walletService,
        Mock<ISeatInventoryRepository> seatRepo,
        Mock<IGameInventoryRepository> gameRepo)
    {
        var outboxRepo = new Mock<IOutboxRepository>();
        outboxRepo.Setup(r => r.AddAsync(It.IsAny<OutboxEvent>()))
            .Returns(Task.CompletedTask);

        var service = new LobbyService(
            lobbyRepo.Object,
            new Mock<IGameTemplateRepository>().Object,
            new Mock<IUserManagementRepository>().Object,
            new Mock<ILobbyInviteRepository>().Object,
            new Mock<ILobbyHubService>().Object,
            new Mock<ILobbyMessageService>().Object,
            new Mock<ILobbyMessageRepository>().Object,
            new Mock<IFriendshipRepository>().Object,
            reservationRepo.Object,
            walletService.Object,
            seatRepo.Object,
            gameRepo.Object,
            outboxRepo.Object,
            new Mock<ICafeRepository>().Object,
            new Mock<BoardVerseDbContext>(new DbContextOptions<BoardVerseDbContext>()).Object,
            new EligibilityValidator(),
            new Mock<IUserProfileService>().Object,
            new Mock<ILogger<LobbyService>>().Object);

        return (service, outboxRepo);
    }

    private static Lobby MakeLobby(
        Guid lobbyId,
        Guid hostId,
        LobbyStatus status,
        DateTime createdAt,
        int maxMembers = 4,
        Guid? reservationId = null)
    {
        return new Lobby
        {
            Id = lobbyId,
            Status = status,
            MaxMembers = maxMembers,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            ReservationId = reservationId,
            Members = new List<LobbyMember>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = hostId,
                    IsActive = true,
                    IsHost = true
                }
            }
        };
    }

    private static Reservation MakeReservation(
        Guid reservationId,
        Guid hostId,
        long depositAmount,
        DateTime scheduledStart,
        DateOnly playDate = default,
        Guid cafeId = default,
        Guid gameId = default)
    {
        return new Reservation
        {
            Id = reservationId,
            HostId = hostId,
            Status = ReservationStatus.Holding,
            DepositAmount = depositAmount,
            ScheduledStartTime = scheduledStart,
            CafeId = cafeId == Guid.Empty ? Guid.NewGuid() : cafeId,
            GameId = gameId == Guid.Empty ? Guid.NewGuid() : gameId,
            PlayDate = playDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : playDate,
            TimeSlot = TimeSlot.Evening
        };
    }

    [Fact]
    public async Task DissolveLobbyAsync_WithGracePeriodNoMember_RefundsFullDeposit()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddMinutes(-5), reservationId: reservationId);
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 120L,
            scheduledStart: DateTime.UtcNow.AddHours(2));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);

        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ReleaseDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var service = BuildService(lobbyRepo, reservationRepo, walletService,
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        var result = await service.DissolveLobbyAsync(lobbyId, hostId);

        Assert.Equal(lobbyId, result.LobbyId);
        Assert.Equal(120L, result.RefundBvc);
        Assert.Equal(0L, result.ForfeitBvc);
        Assert.Equal("Grace-15p-NoMember", result.RefundPolicyApplied);

        walletService.Verify(w => w.ReleaseDepositAsync(
            hostId, 120L, lobbyId, reservationId,
            $"dissolve-refund-{lobbyId:N}",
            It.IsAny<CancellationToken>()), Times.Once);
        walletService.Verify(w => w.ForfeitDepositAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DissolveLobbyAsync_CancelAtLeast24hBefore_RefundsFullDeposit()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddMinutes(-30), reservationId: reservationId);
        lobby.Members.Add(new LobbyMember
        {
            Id = Guid.NewGuid(),
            UserId = member2Id,
            IsActive = true,
            IsHost = false
        });
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 100L,
            scheduledStart: DateTime.UtcNow.AddHours(48));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);

        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ReleaseDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var service = BuildService(lobbyRepo, reservationRepo, walletService,
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        var result = await service.DissolveLobbyAsync(lobbyId, hostId);

        Assert.Equal(100L, result.RefundBvc);
        Assert.Equal(0L, result.ForfeitBvc);
        Assert.Equal("Cancel-24h", result.RefundPolicyApplied);
    }

    [Fact]
    public async Task DissolveLobbyAsync_Cancel6To24hBefore_Refunds50PercentAndForfeits50()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddHours(-2), reservationId: reservationId);
        lobby.Members.Add(new LobbyMember
        {
            Id = Guid.NewGuid(),
            UserId = member2Id,
            IsActive = true,
            IsHost = false
        });
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 100L,
            scheduledStart: DateTime.UtcNow.AddHours(12));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);

        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ReleaseDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());
        walletService.Setup(w => w.ForfeitDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var service = BuildService(lobbyRepo, reservationRepo, walletService,
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        var result = await service.DissolveLobbyAsync(lobbyId, hostId);

        Assert.Equal(50L, result.RefundBvc);
        Assert.Equal(50L, result.ForfeitBvc);
        Assert.Equal("Cancel-6h", result.RefundPolicyApplied);
    }

    [Fact]
    public async Task DissolveLobbyAsync_CancelUnder6hBefore_ForfeitsFullDeposit()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddHours(-2), reservationId: reservationId);
        lobby.Members.Add(new LobbyMember
        {
            Id = Guid.NewGuid(),
            UserId = member2Id,
            IsActive = true,
            IsHost = false
        });
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 100L,
            scheduledStart: DateTime.UtcNow.AddHours(3));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);

        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ForfeitDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var service = BuildService(lobbyRepo, reservationRepo, walletService,
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        var result = await service.DissolveLobbyAsync(lobbyId, hostId);

        Assert.Equal(0L, result.RefundBvc);
        Assert.Equal(100L, result.ForfeitBvc);
        Assert.Equal("Cancel-Under6h", result.RefundPolicyApplied);

        walletService.Verify(w => w.ReleaseDepositAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        walletService.Verify(w => w.ForfeitDepositAsync(
            hostId, 100L, lobbyId, reservationId,
            $"dissolve-forfeit-{lobbyId:N}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DissolveLobbyAsync_LobbyAlreadyViable_ThrowsConflict()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Viable,
            createdAt: DateTime.UtcNow.AddMinutes(-30));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var service = BuildService(lobbyRepo,
            new Mock<IReservationRepository>(), new Mock<IWalletService>(),
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await Assert.ThrowsAsync<ConflictException>(
            () => service.DissolveLobbyAsync(lobbyId, hostId));
    }

    [Fact]
    public async Task DissolveLobbyAsync_LobbyAlreadyDissolved_ThrowsConflict()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Dissolved,
            createdAt: DateTime.UtcNow.AddMinutes(-30));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var service = BuildService(lobbyRepo,
            new Mock<IReservationRepository>(), new Mock<IWalletService>(),
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await Assert.ThrowsAsync<ConflictException>(
            () => service.DissolveLobbyAsync(lobbyId, hostId));
    }

    [Fact]
    public async Task DissolveLobbyAsync_NotHost_ThrowsForbidden()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var service = BuildService(lobbyRepo,
            new Mock<IReservationRepository>(), new Mock<IWalletService>(),
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.DissolveLobbyAsync(lobbyId, strangerId));
    }

    [Fact]
    public async Task DissolveLobbyAsync_LobbyNotFound_ThrowsNotFound()
    {
        var lobbyId = Guid.NewGuid();
        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync((Lobby?)null);

        var service = BuildService(lobbyRepo,
            new Mock<IReservationRepository>(), new Mock<IWalletService>(),
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.DissolveLobbyAsync(lobbyId, Guid.NewGuid()));
    }

    [Fact]
    public async Task DissolveLobbyAsync_ReleasesSeatInventoryAndGameInventory()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddMinutes(-5),
            maxMembers: 4, reservationId: reservationId);
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 50L,
            scheduledStart: DateTime.UtcNow.AddHours(48),
            playDate: playDate, cafeId: cafeId, gameId: gameId);
        reservation.SeatInventoryId = Guid.NewGuid();
        reservation.GameInventoryId = Guid.NewGuid();

        var seatInv = new SeatInventory
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            PlayDate = playDate,
            ScheduledStartTime = new TimeOnly(17, 0),
            ScheduledEndTime = new TimeOnly(23, 0),
            TotalSeats = 20,
            HeldSeats = 4,
            InUseSeats = 0,
            UpdatedAt = DateTime.UtcNow
        };
        var gameInv = new GameInventory
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            GameId = gameId,
            PlayDate = playDate,
            ScheduledStartTime = new TimeOnly(17, 0),
            ScheduledEndTime = new TimeOnly(23, 0),
            TotalCopies = 3,
            HeldCopies = 1,
            InUseCopies = 0,
            UpdatedAt = DateTime.UtcNow
        };

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);

        var seatRepo = new Mock<ISeatInventoryRepository>();
        seatRepo.Setup(r => r.GetForUpdateAsync(cafeId, playDate, new TimeOnly(17, 0), new TimeOnly(23, 0)))
            .ReturnsAsync(seatInv);
        var gameRepo = new Mock<IGameInventoryRepository>();
        gameRepo.Setup(r => r.GetForUpdateAsync(cafeId, gameId, playDate, new TimeOnly(17, 0), new TimeOnly(23, 0)))
            .ReturnsAsync(gameInv);

        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ReleaseDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var service = BuildService(lobbyRepo, reservationRepo, walletService, seatRepo, gameRepo);

        await service.DissolveLobbyAsync(lobbyId, hostId);

        Assert.Equal(0, seatInv.HeldSeats);
        Assert.Equal(0, gameInv.HeldCopies);

        seatRepo.Verify(r => r.UpdateAsync(seatInv), Times.Once);
        gameRepo.Verify(r => r.UpdateAsync(gameInv), Times.Once);
    }

    [Fact]
    public async Task DissolveLobbyAsync_FlipsLobbyStatusToDissolved()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var service = BuildService(lobbyRepo,
            new Mock<IReservationRepository>(), new Mock<IWalletService>(),
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        var result = await service.DissolveLobbyAsync(lobbyId, hostId);

        Assert.Equal(LobbyStatus.Dissolved, lobby.Status);
        Assert.NotNull(lobby.ClosedAt);
        Assert.NotNull(lobby.ClosedReason);
        Assert.True(lobby.Members.All(m => !m.IsActive));
        Assert.Equal(lobbyId, result.LobbyId);
    }

    [Fact]
    public async Task DissolveLobbyAsync_FlipsReservationToCancelledByPlayer()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddMinutes(-5), reservationId: reservationId);
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 80L,
            scheduledStart: DateTime.UtcNow.AddHours(48));

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);
        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ReleaseDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var service = BuildService(lobbyRepo, reservationRepo, walletService,
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await service.DissolveLobbyAsync(lobbyId, hostId);

        Assert.Equal(ReservationStatus.CancelledByPlayer, reservation.Status);
        reservationRepo.Verify(r => r.UpdateAsync(reservation), Times.Once);
    }

    // ===== Gap #5: ShareCode + IsPrivate nullify =====

    [Fact]
    public async Task DissolveLobbyAsync_NullifiesShareCodeAndIsPrivate_OnDissolved()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddMinutes(-5));
        lobby.IsPrivate = true;
        lobby.ShareCode = "ABC123";

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);

        var service = BuildService(lobbyRepo,
            new Mock<IReservationRepository>(), new Mock<IWalletService>(),
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await service.DissolveLobbyAsync(lobbyId, hostId);

        Assert.False(lobby.IsPrivate);
        Assert.Equal(string.Empty, lobby.ShareCode);
        Assert.Equal(LobbyStatus.Dissolved, lobby.Status);
    }

    // ===== Gap #2: Outbox events =====

    [Fact]
    public async Task DissolveLobbyAsync_EmitsLobbyCancelledByHostAndDepositReleasedOutbox_OnFullRefund()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddMinutes(-5), reservationId: reservationId);
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 100L,
            scheduledStart: DateTime.UtcNow.AddHours(48)); // Cancel-24h → 100% refund

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);

        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ReleaseDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var (service, outboxRepo) = BuildServiceWithOutboxCapture(
            lobbyRepo, reservationRepo, walletService,
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await service.DissolveLobbyAsync(lobbyId, hostId);

        // Capture tất cả OutboxEvent được AddAsync.
        var addedEvents = new List<OutboxEvent>();
        outboxRepo.Verify(r => r.AddAsync(It.IsAny<OutboxEvent>()), Times.Exactly(2));
        outboxRepo.Invocations
            .Where(i => i.Method.Name == nameof(IOutboxRepository.AddAsync))
            .ToList()
            .ForEach(i => addedEvents.Add((OutboxEvent)i.Arguments[0]!));

        Assert.Contains(addedEvents, e =>
            e.EventType == OutboxEventType.LobbyCancelledByHost
            && e.IdempotencyKey == $"dissolve-lobby-cancelled-{lobbyId:N}"
            && e.LobbyId == lobbyId
            && e.UserId == hostId);

        Assert.Contains(addedEvents, e =>
            e.EventType == OutboxEventType.DepositReleased
            && e.IdempotencyKey == $"dissolve-refund-{lobbyId:N}"
            && e.LobbyId == lobbyId
            && e.UserId == hostId);

        // Under6h forfeit flow không được emit DepositReleased khi full refund.
        Assert.DoesNotContain(addedEvents, e => e.EventType == OutboxEventType.DepositCaptured);
    }

    [Fact]
    public async Task DissolveLobbyAsync_EmitsDepositCapturedOutbox_WhenForfeitNonZero()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddHours(-2), reservationId: reservationId);
        lobby.Members.Add(new LobbyMember
        {
            Id = Guid.NewGuid(),
            UserId = member2Id,
            IsActive = true,
            IsHost = false
        });
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 100L,
            scheduledStart: DateTime.UtcNow.AddHours(3)); // <6h → 100% forfeit

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);

        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ForfeitDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var (service, outboxRepo) = BuildServiceWithOutboxCapture(
            lobbyRepo, reservationRepo, walletService,
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await service.DissolveLobbyAsync(lobbyId, hostId);

        var addedEvents = new List<OutboxEvent>();
        outboxRepo.Invocations
            .Where(i => i.Method.Name == nameof(IOutboxRepository.AddAsync))
            .ToList()
            .ForEach(i => addedEvents.Add((OutboxEvent)i.Arguments[0]!));

        // Under6h flow chỉ có Captured (không có Released).
        Assert.Contains(addedEvents, e =>
            e.EventType == OutboxEventType.LobbyCancelledByHost
            && e.IdempotencyKey == $"dissolve-lobby-cancelled-{lobbyId:N}");

        Assert.Contains(addedEvents, e =>
            e.EventType == OutboxEventType.DepositCaptured
            && e.IdempotencyKey == $"dissolve-forfeit-{lobbyId:N}");

        Assert.DoesNotContain(addedEvents, e => e.EventType == OutboxEventType.DepositReleased);
    }

    [Fact]
    public async Task DissolveLobbyAsync_EmitsBothReleasedAndCapturedOutbox_On6To24hPartialRefund()
    {
        var lobbyId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobby = MakeLobby(lobbyId, hostId, LobbyStatus.Open,
            createdAt: DateTime.UtcNow.AddHours(-2), reservationId: reservationId);
        lobby.Members.Add(new LobbyMember
        {
            Id = Guid.NewGuid(),
            UserId = member2Id,
            IsActive = true,
            IsHost = false
        });
        var reservation = MakeReservation(reservationId, hostId,
            depositAmount: 100L,
            scheduledStart: DateTime.UtcNow.AddHours(12)); // 6-24h → 50/50

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId)).ReturnsAsync(lobby);
        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByIdAsync(reservationId, It.IsAny<bool>())).ReturnsAsync(reservation);

        var walletService = new Mock<IWalletService>();
        walletService.Setup(w => w.ReleaseDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());
        walletService.Setup(w => w.ForfeitDepositAsync(It.IsAny<Guid>(), It.IsAny<long>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcHoldResult());

        var (service, outboxRepo) = BuildServiceWithOutboxCapture(
            lobbyRepo, reservationRepo, walletService,
            new Mock<ISeatInventoryRepository>(), new Mock<IGameInventoryRepository>());

        await service.DissolveLobbyAsync(lobbyId, hostId);

        var addedEvents = new List<OutboxEvent>();
        outboxRepo.Invocations
            .Where(i => i.Method.Name == nameof(IOutboxRepository.AddAsync))
            .ToList()
            .ForEach(i => addedEvents.Add((OutboxEvent)i.Arguments[0]!));

        // 6-24h → cả Released lẫn Captured đều emit.
        outboxRepo.Verify(r => r.AddAsync(It.IsAny<OutboxEvent>()), Times.Exactly(3));
        Assert.Contains(addedEvents, e => e.EventType == OutboxEventType.LobbyCancelledByHost);
        Assert.Contains(addedEvents, e => e.EventType == OutboxEventType.DepositReleased);
        Assert.Contains(addedEvents, e => e.EventType == OutboxEventType.DepositCaptured);
    }
}
