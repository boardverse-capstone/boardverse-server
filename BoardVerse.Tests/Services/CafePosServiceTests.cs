using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class CafePosServiceTests
{
    private static readonly Guid CafeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ManagerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TableId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
    private static readonly Guid BoxId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GameTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid HostId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static CafePosService CreateService(
        Mock<ICafePosRepository>? posRepo = null,
        Mock<ICafeRepository>? cafeRepo = null,
        Mock<IBookingDepositRepository>? depositRepo = null,
        Mock<IActiveSessionRepository>? activeSessionRepo = null,
        Mock<IPosHubService>? posHubService = null,
        Mock<ILobbyRepository>? lobbyRepo = null,
        Mock<IUserProfileRepository>? userProfileRepo = null)
    {
        return new CafePosService(
            posRepo?.Object ?? new Mock<ICafePosRepository>().Object,
            cafeRepo?.Object ?? new Mock<ICafeRepository>().Object,
            depositRepo?.Object ?? new Mock<IBookingDepositRepository>().Object,
            activeSessionRepo?.Object ?? new Mock<IActiveSessionRepository>().Object,
            posHubService?.Object ?? new Mock<IPosHubService>().Object,
            lobbyRepo?.Object ?? new Mock<ILobbyRepository>().Object,
            userProfileRepo?.Object ?? new Mock<IUserProfileRepository>().Object
        );
    }

    [Fact]
    public async Task StartGameSessionAsync_Success_StartsSessionAndUpdatesStatuses()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var table = new CafeTable { Id = TableId, CafeId = CafeId, Name = "T1", Status = CafeTableStatus.Available };
        var gameTemplate = new GameTemplate { Id = GameTemplateId, Name = "Catan", PlayTime = 90 };
        var inventory = new CafeGameInventory { Id = Guid.NewGuid(), CafeId = CafeId, GameTemplateId = GameTemplateId, GameTemplate = gameTemplate };
        var box = new CafeInventoryBox
        {
            Id = BoxId,
            Barcode = "BV-test-001",
            Status = CafeGameInventoryStatus.Available,
            CafeGameInventory = inventory
        };

        posRepo.Setup(r => r.GetTableAsync(CafeId, TableId)).ReturnsAsync(table);
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(CafeId, "BV-test-001")).ReturnsAsync(box);
        posRepo.Setup(r => r.GetActiveSessionByBoxIdAsync(BoxId)).ReturnsAsync((ActiveSession?)null);

        var service = CreateService(posRepo, cafeRepo);
        var result = await service.StartGameSessionAsync(CafeId, ManagerId, "Manager", new StartGameSessionRequestDto
        {
            CafeTableId = TableId,
            Barcode = "BV-test-001"
        });

        Assert.Equal(TableId, result.CafeTableId);
        Assert.Equal(ManagerId, result.HostId);
        Assert.Equal(CafeGameInventoryStatus.InUse, box.Status);
        Assert.Equal(CafeTableStatus.InUse, table.Status);
        posRepo.Verify(r => r.AddSessionAsync(It.IsAny<ActiveSession>()), Times.Once);
        posRepo.Verify(r => r.AddSessionMemberAsync(It.IsAny<ActiveSessionMember>()), Times.Once);
        posRepo.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartGameSessionAsync_BoxNotAvailable_ThrowsConflict()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        posRepo.Setup(r => r.GetTableAsync(CafeId, TableId))
            .ReturnsAsync(new CafeTable { Id = TableId, Status = CafeTableStatus.Available });
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(CafeId, "BV-test-001"))
            .ReturnsAsync(new CafeInventoryBox
            {
                Id = BoxId,
                Barcode = "BV-test-001",
                Status = CafeGameInventoryStatus.InUse,
                CafeGameInventory = new CafeGameInventory { GameTemplateId = GameTemplateId }
            });

        var service = CreateService(posRepo, cafeRepo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartGameSessionAsync(CafeId, ManagerId, "Manager", new StartGameSessionRequestDto
            {
                CafeTableId = TableId,
                Barcode = "BV-test-001"
            }));
    }

    [Fact]
    public async Task StartGameSessionAsync_ReservedTable_ThrowsConflict()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);
        posRepo.Setup(r => r.GetTableAsync(CafeId, TableId))
            .ReturnsAsync(new CafeTable { Id = TableId, Status = CafeTableStatus.Reserved });

        var service = CreateService(posRepo, cafeRepo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartGameSessionAsync(CafeId, ManagerId, "Manager", new StartGameSessionRequestDto
            {
                CafeTableId = TableId,
                Barcode = "BV-test-001"
            }));
    }

    [Fact]
    public async Task EndGameSessionAsync_ReleasesBoxAndTable()
    {
        var sessionId = Guid.NewGuid();
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var table = new CafeTable { Id = TableId, Status = CafeTableStatus.InUse, Name = "T1" };
        var box = new CafeInventoryBox { Id = BoxId, Barcode = "BV-test-001", Status = CafeGameInventoryStatus.InUse };
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = CafeId,
            CafeTableId = TableId,
            CafeTable = table,
            CafeInventoryBoxId = BoxId,
            CafeInventoryBox = box,
            GameTemplateId = GameTemplateId,
            GameTemplate = new GameTemplate { Name = "Catan", PlayTime = 90 },
            StartedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        posRepo.Setup(r => r.GetActiveSessionByIdAsync(CafeId, sessionId)).ReturnsAsync(session);
        posRepo.Setup(r => r.GetActiveSessionsAsync(CafeId, null)).ReturnsAsync([]);

        var service = CreateService(posRepo, cafeRepo);
        var result = await service.EndGameSessionAsync(CafeId, ManagerId, "Manager", sessionId);

        Assert.Equal(CafeGameInventoryStatus.Available, box.Status);
        Assert.Equal(CafeTableStatus.Available, table.Status);
        Assert.Equal(sessionId, result.Id);
    }

    [Fact]
    public async Task GetBoxByBarcodeAsync_EmptyBarcode_ThrowsBadRequest()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var service = CreateService(posRepo, cafeRepo);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetBoxByBarcodeAsync(CafeId, ManagerId, "Manager", "   "));
    }

    [Fact]
    public async Task GetTablesAsync_NoAccess_ThrowsForbidden()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Player")).ReturnsAsync(false);

        var service = CreateService(posRepo, cafeRepo);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetTablesAsync(CafeId, ManagerId, "Player"));
    }

    #region GetBookingPreviewAsync Tests

    [Fact]
    public async Task GetBookingPreviewAsync_BookingNotFound_ThrowsNotFound()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByBookingCodeAsync("BV123"))
            .ReturnsAsync((BookingDeposit?)null);

        var service = CreateService(posRepo, cafeRepo, depositRepo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetBookingPreviewAsync(CafeId, ManagerId, "Manager", "BV123"));
    }

    [Fact]
    public async Task GetBookingPreviewAsync_WrongCafe_ThrowsConflict()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            OrderId = "BV123",
            UserId = HostId,
            CafeId = Guid.NewGuid(), // Different cafe
            Amount = 50000,
            Status = BookingDepositStatus.Paid
        };

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByBookingCodeAsync("BV123")).ReturnsAsync(deposit);

        var service = CreateService(posRepo, cafeRepo, depositRepo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.GetBookingPreviewAsync(CafeId, ManagerId, "Manager", "BV123"));
    }

    [Fact]
    public async Task GetBookingPreviewAsync_DepositNotPaid_ReturnsCanCheckInFalse()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            OrderId = "BV123",
            UserId = HostId,
            CafeId = CafeId,
            Amount = 50000,
            Status = BookingDepositStatus.Pending // Not paid
        };

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByBookingCodeAsync("BV123")).ReturnsAsync(deposit);

        var userProfileRepo = new Mock<IUserProfileRepository>();
        userProfileRepo.Setup(r => r.GetByIdWithProfileAsync(HostId))
            .ReturnsAsync(new User { Id = HostId, Username = "testuser", Email = "test@test.com" });

        var service = CreateService(posRepo, cafeRepo, depositRepo, userProfileRepo: userProfileRepo);
        var result = await service.GetBookingPreviewAsync(CafeId, ManagerId, "Manager", "BV123");

        Assert.False(result.CanCheckIn);
        Assert.Equal("Đơn cọc chưa thanh toán.", result.CannotCheckInReason);
    }

    [Fact]
    public async Task GetBookingPreviewAsync_DepositPaid_ReturnsCanCheckInTrue()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            OrderId = "BV123",
            UserId = HostId,
            CafeId = CafeId,
            Amount = 50000,
            Status = BookingDepositStatus.Paid,
            ScheduledAt = DateTime.UtcNow.AddHours(1)
        };

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByBookingCodeAsync("BV123")).ReturnsAsync(deposit);

        var userProfileRepo = new Mock<IUserProfileRepository>();
        userProfileRepo.Setup(r => r.GetByIdWithProfileAsync(HostId))
            .ReturnsAsync(new User 
            { 
                Id = HostId, 
                Username = "testuser",
                Email = "test@test.com",
                Profile = new UserProfile { KarmaPoints = 85 }
            });

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByActiveSessionIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Lobby?)null);

        var service = CreateService(posRepo, cafeRepo, depositRepo, lobbyRepo: lobbyRepo, userProfileRepo: userProfileRepo);
        var result = await service.GetBookingPreviewAsync(CafeId, ManagerId, "Manager", "BV123");

        Assert.True(result.CanCheckIn);
        Assert.Null(result.CannotCheckInReason);
        Assert.Equal("BV123", result.BookingCode);
        Assert.Equal(50000, result.DepositAmount);
        Assert.NotNull(result.Host);
        Assert.Equal(HostId, result.Host.UserId);
    }

    #endregion

    #region StartSessionFromBookingAsync Tests

    [Fact]
    public async Task StartSessionFromBookingAsync_DepositNotPaid_ThrowsConflict()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            OrderId = "BV123",
            UserId = HostId,
            CafeId = CafeId,
            Status = BookingDepositStatus.Pending
        };

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByBookingCodeAsync("BV123")).ReturnsAsync(deposit);

        var service = CreateService(posRepo, cafeRepo, depositRepo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartSessionFromBookingAsync(CafeId, ManagerId, "Manager", new StartSessionFromBookingRequestDto
            {
                BookingCode = "BV123",
                CafeTableId = TableId,
                Barcode = "BV-test-001"
            }));
    }

    [Fact]
    public async Task StartSessionFromBookingAsync_Success_CreatesSessionAndSendsNotification()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var table = new CafeTable { Id = TableId, CafeId = CafeId, Name = "T1", Status = CafeTableStatus.Available };
        var gameTemplate = new GameTemplate { Id = GameTemplateId, Name = "Catan", PlayTime = 90 };
        var inventory = new CafeGameInventory { Id = BoxId, CafeId = CafeId, GameTemplateId = GameTemplateId, GameTemplate = gameTemplate };
        var box = new CafeInventoryBox
        {
            Id = BoxId,
            Barcode = "BV-test-001",
            Status = CafeGameInventoryStatus.Available,
            CafeGameInventory = inventory
        };

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            OrderId = "BV123",
            UserId = HostId,
            CafeId = CafeId,
            Amount = 50000,
            Status = BookingDepositStatus.Paid,
            ActiveSessionId = null // Not yet checked in
        };

        var cafe = new Cafe { Id = CafeId, Name = "Demo Cafe", Address = "Test Address", IsActive = true };

        posRepo.Setup(r => r.GetTableAsync(CafeId, TableId)).ReturnsAsync(table);
        posRepo.Setup(r => r.GetBoxByBarcodeAsync(CafeId, "BV-test-001")).ReturnsAsync(box);
        posRepo.Setup(r => r.GetActiveSessionByBoxIdAsync(BoxId)).ReturnsAsync((ActiveSession?)null);

        var depositRepo = new Mock<IBookingDepositRepository>();
        depositRepo.Setup(r => r.GetByBookingCodeAsync("BV123")).ReturnsAsync(deposit);

        cafeRepo.Setup(r => r.GetByIdAsync(CafeId)).ReturnsAsync(cafe);

        var posHubService = new Mock<IPosHubService>();
        posHubService.Setup(s => s.NotifySessionActivatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>()))
            .Returns(Task.CompletedTask);

        var lobbyRepo = new Mock<ILobbyRepository>();
        lobbyRepo.Setup(r => r.GetByActiveSessionIdAsync(It.IsAny<Guid>())).ReturnsAsync((Lobby?)null);

        var service = CreateService(posRepo, cafeRepo, depositRepo, 
            posHubService: posHubService, lobbyRepo: lobbyRepo);
        
        var result = await service.StartSessionFromBookingAsync(CafeId, ManagerId, "Manager", new StartSessionFromBookingRequestDto
        {
            BookingCode = "BV123",
            CafeTableId = TableId,
            Barcode = "BV-test-001"
        });

        Assert.NotNull(result);
        Assert.Equal(TableId, result.CafeTableId);
        Assert.Equal(HostId, result.HostId);
        Assert.Equal(CafeGameInventoryStatus.InUse, box.Status);
        Assert.Equal(CafeTableStatus.InUse, table.Status);
        
        // Verify SignalR notification was sent
        posHubService.Verify(s => s.NotifySessionActivatedAsync(
            It.IsAny<Guid>(), CafeId, "Demo Cafe", HostId, It.IsAny<IReadOnlyList<Guid>>()), Times.Once);
    }

    #endregion

    #region ReturnGameAsync Tests

    [Fact]
    public async Task ReturnGameAsync_SessionNotFound_ThrowsNotFound()
    {
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var service = CreateService(posRepo, cafeRepo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ReturnGameAsync(CafeId, ManagerId, "Manager", Guid.NewGuid(), new ReturnGameRequestDto
            {
                InventoryBoxId = BoxId,
                DamagedComponents = []
            }));
    }

    [Fact]
    public async Task ReturnGameAsync_BoxNotFound_ThrowsNotFound()
    {
        var sessionId = Guid.NewGuid();
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = CafeId,
            CafeInventoryBoxId = BoxId
        };

        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        activeSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        posRepo.Setup(r => r.GetInventoryBoxByIdAsync(BoxId)).ReturnsAsync((CafeInventoryBox?)null);

        var service = CreateService(posRepo, cafeRepo, activeSessionRepo: activeSessionRepo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ReturnGameAsync(CafeId, ManagerId, "Manager", sessionId, new ReturnGameRequestDto
            {
                InventoryBoxId = BoxId,
                DamagedComponents = []
            }));
    }

    [Fact]
    public async Task ReturnGameAsync_NoDamagedComponents_ReturnsZeroFine()
    {
        var sessionId = Guid.NewGuid();
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(CafeId))
            .ReturnsAsync(new Cafe { Id = CafeId, Name = "Demo", Address = "Addr", IsActive = true });
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = CafeId,
            SurchargeFine = 0
        };

        var inventory = new CafeGameInventory
        {
            Id = Guid.NewGuid(),
            CafeId = CafeId,
            GameTemplateId = GameTemplateId,
            ComponentPenalties = []
        };

        var box = new CafeInventoryBox
        {
            Id = BoxId,
            CafeGameInventory = inventory,
            Status = CafeGameInventoryStatus.InUse
        };

        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        activeSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        posRepo.Setup(r => r.GetInventoryBoxByIdAsync(BoxId)).ReturnsAsync(box);

        var service = CreateService(posRepo, cafeRepo, activeSessionRepo: activeSessionRepo);
        var result = await service.ReturnGameAsync(CafeId, ManagerId, "Manager", sessionId, new ReturnGameRequestDto
        {
            InventoryBoxId = BoxId,
            DamagedComponents = []
        });

        Assert.Equal(0, result.SurchargeFine);
        Assert.False(result.HasDamagedComponents);
        Assert.Equal("InUse", result.BoxMaintenanceStatus);
        activeSessionRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ReturnGameAsync_WithDamagedComponents_CalculatesFine()
    {
        var sessionId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var posRepo = new Mock<ICafePosRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        SetupActiveCafe(cafeRepo);
        posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager")).ReturnsAsync(true);

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = CafeId,
            SurchargeFine = 0
        };

        var penalty = new CafeGameComponentPenalty
        {
            Id = Guid.NewGuid(),
            GameComponentTemplateId = componentId,
            PenaltyFee = 15000 // 15,000 VND per item
        };

        var inventory = new CafeGameInventory
        {
            Id = Guid.NewGuid(),
            CafeId = CafeId,
            GameTemplateId = GameTemplateId,
            ComponentPenalties = [penalty]
        };

        var box = new CafeInventoryBox
        {
            Id = BoxId,
            CafeGameInventory = inventory,
            Status = CafeGameInventoryStatus.InUse
        };

        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        activeSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        posRepo.Setup(r => r.GetInventoryBoxByIdAsync(BoxId)).ReturnsAsync(box);

        var service = CreateService(posRepo, cafeRepo, activeSessionRepo: activeSessionRepo);
        var result = await service.ReturnGameAsync(CafeId, ManagerId, "Manager", sessionId, new ReturnGameRequestDto
        {
            InventoryBoxId = BoxId,
            DamagedComponents =
            [
                new DamagedComponentDto { ComponentId = componentId, MissingQuantity = 2, DamagedQuantity = 1 }
            ]
        });

        // 2 missing * 15,000 + 1 damaged * 15,000 = 45,000
        Assert.Equal(45000, result.SurchargeFine);
        Assert.True(result.HasDamagedComponents);
        Assert.Equal("Maintenance", result.BoxMaintenanceStatus);
        Assert.Equal(CafeGameInventoryStatus.Maintenance, box.Status);
        activeSessionRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    private static void SetupActiveCafe(Mock<ICafeRepository> cafeRepo) =>
        cafeRepo.Setup(r => r.GetActiveByIdAsync(CafeId))
            .ReturnsAsync(new Cafe { Id = CafeId, Name = "Demo", Address = "Test Address", IsActive = true });
}
