using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho CafePosService — sync tables, boxes, sessions, lookup-by-barcode.
/// </summary>
public class CafePosServiceTests
{
    private readonly Mock<ICafePosRepository> _posRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<IBookingDepositRepository> _depositRepo = new();
    private readonly Mock<IBookingRepository> _bookingRepo = new();
    private readonly Mock<IActiveSessionRepository> _activeSessionRepo = new();
    private readonly Mock<IActiveSessionService> _activeSessionService = new();
    private readonly Mock<IPosHubService> _posHubService = new();
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<IUserProfileRepository> _userProfileRepo = new();
    private readonly Mock<IReservationService> _reservationService = new();
    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<IPosCheckInTokenRepository> _tokenRepo = new();
    private readonly Mock<ILogger<CafePosService>> _logger = new();
    private readonly BoardVerseDbContext _db;

    private static readonly Guid CafeId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid ManagerId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
    private static readonly Guid BoxId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
    private static readonly Guid GameTemplateId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");
    private static readonly Guid SessionId = Guid.Parse("eeeeeeee-5555-5555-5555-555555555555");

    private static readonly MemoryCache MemoryCache = new(new MemoryCacheOptions());

    public CafePosServiceTests()
    {
        _db = new FakeDbContext();
        // Default: manager có quyền operate cafe
        _posRepo.Setup(r => r.CanOperateCafeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // Default: cafe tồn tại và active (EnsurePosAccessAsync lookup)
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe());
    }

    private CafePosService CreateService() => new(
        _posRepo.Object,
        _cafeRepo.Object,
        _depositRepo.Object,
        _bookingRepo.Object,
        _activeSessionRepo.Object,
        _activeSessionService.Object,
        _posHubService.Object,
        _lobbyRepo.Object,
        _userProfileRepo.Object,
        _reservationService.Object,
        _reservationRepo.Object,
        _tokenRepo.Object,
        MemoryCache,
        _logger.Object,
        _db);

    private static Cafe BuildCafe() => new()
    {
        Id = CafeId,
        ManagerId = ManagerId,
        Name = "Test Cafe",
        Address = "1 Test Street",
        IsActive = true
    };

    private static CafeInventoryBox BuildBox(string barcode, CafeGameInventoryStatus status = CafeGameInventoryStatus.Available) => new()
    {
        Id = BoxId,
        CafeGameInventoryId = GameTemplateId,
        Barcode = barcode,
        Status = status,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        CafeGameInventory = new CafeGameInventory
        {
            Id = GameTemplateId,
            GameTemplateId = GameTemplateId,
            CafeId = CafeId,
            GameTemplate = new GameTemplate
            {
                Id = GameTemplateId,
                Name = "Catan"
            }
        }
    };

    // ===================== GetBoxesAsync =====================

    [Fact]
    public async Task GetBoxesAsync_ReturnsMappedBoxes()
    {
        _posRepo.Setup(r => r.GetBoxesAsync(CafeId, GameTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeInventoryBox> { BuildBox("BV-001") });

        var service = CreateService();

        var result = await service.GetBoxesAsync(CafeId, ManagerId, "Manager", GameTemplateId);

        Assert.Single(result);
        Assert.Equal("BV-001", result[0].Barcode);
    }

    [Fact]
    public async Task GetBoxesAsync_NoGameFilter_PassesNullGameTemplateId()
    {
        _posRepo.Setup(r => r.GetBoxesAsync(CafeId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeInventoryBox>());

        var service = CreateService();

        var result = await service.GetBoxesAsync(CafeId, ManagerId, "Manager", null);

        Assert.Empty(result);
        _posRepo.Verify(r => r.GetBoxesAsync(CafeId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===================== GetBoxByBarcodeAsync =====================

    [Fact]
    public async Task GetBoxByBarcodeAsync_TrimsWhitespace()
    {
        _posRepo.Setup(r => r.GetBoxByBarcodeAsync(CafeId, "BV-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBox("BV-001"));

        var service = CreateService();

        var result = await service.GetBoxByBarcodeAsync(CafeId, ManagerId, "Manager", "  BV-001  ");

        Assert.NotNull(result);
        Assert.Equal("BV-001", result.Barcode);
        _posRepo.Verify(r => r.GetBoxByBarcodeAsync(CafeId, "BV-001", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBoxByBarcodeAsync_EmptyBarcode_ThrowsBadRequest()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(
            () => service.GetBoxByBarcodeAsync(CafeId, ManagerId, "Manager", "   "));
    }

    [Fact]
    public async Task GetBoxByBarcodeAsync_BoxNotFound_ThrowsNotFound()
    {
        _posRepo.Setup(r => r.GetBoxByBarcodeAsync(CafeId, "MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeInventoryBox?)null);

        var service = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetBoxByBarcodeAsync(CafeId, ManagerId, "Manager", "MISSING"));
    }

    // ===================== GetSessionByIdAsync =====================

    [Fact]
    public async Task GetSessionByIdAsync_SessionNotFound_ThrowsNotFound()
    {
        _posRepo.Setup(r => r.GetActiveSessionByIdAsync(CafeId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveSession?)null);

        var service = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetSessionByIdAsync(CafeId, ManagerId, "Manager", SessionId));
    }

    [Fact]
    public async Task GetSessionByIdAsync_Found_ReturnsMappedSession()
    {
        var session = new ActiveSession
        {
            Id = SessionId,
            CafeId = CafeId,
            Status = GroupSessionStatus.Active,
            Members = [],
            Games = [],
            StartedAt = DateTime.UtcNow.AddHours(-1),
        };
        _posRepo.Setup(r => r.GetActiveSessionByIdAsync(CafeId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var service = CreateService();

        var result = await service.GetSessionByIdAsync(CafeId, ManagerId, "Manager", SessionId);

        Assert.NotNull(result);
        Assert.Equal(SessionId, result.Id);
        Assert.Null(result.LobbyId);
    }

    // ===================== SyncTablesAsync (string overload) =====================

    [Fact]
    public async Task SyncTablesAsync_StringNames_ConvertsToCafeTableSyncItems()
    {
        _cafeRepo.Setup(r => r.SyncCafeTablesAsync(CafeId, It.IsAny<IReadOnlyList<CafeTableSyncItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.SyncTablesAsync(CafeId, ManagerId, new[] { "Table 1", "Table 2", "Table 3" });

        _cafeRepo.Verify(r => r.SyncCafeTablesAsync(
            CafeId,
            It.Is<IReadOnlyList<CafeTableSyncItem>>(items =>
                items.Count == 3
                && items[0].Name == "Table 1" && items[0].SortOrder == 0
                && items[1].Name == "Table 2" && items[1].SortOrder == 1
                && items[2].Name == "Table 3" && items[2].SortOrder == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncTablesAsync_DuplicateSortOrder_ThrowsBadRequest()
    {
        // Mô phỏng lỗi từ CafeTableSyncHelper: ArgumentException "SortOrder không được trùng lặp"
        _cafeRepo.Setup(r => r.SyncCafeTablesAsync(CafeId, It.IsAny<IReadOnlyList<CafeTableSyncItem>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("SortOrder không được trùng lặp: 0, 2. Vui lòng đánh số lại."));

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => service.SyncTablesAsync(CafeId, ManagerId, new[]
            {
                new CafeTableSyncItem { Name = "T1", SortOrder = 0 },
                new CafeTableSyncItem { Name = "T2", SortOrder = 0 } // duplicate
            }));
        Assert.Contains("0", ex.Message);
    }

    // ===================== GetActiveSessionsAsync =====================

    [Fact]
    public async Task GetActiveSessionsAsync_NoGameFilter_ReturnsAll()
    {
        _posRepo.Setup(r => r.GetActiveSessionsAsync(CafeId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());

        var service = CreateService();

        var result = await service.GetActiveSessionsAsync(CafeId, ManagerId, "Manager", null);

        Assert.Empty(result);
        _posRepo.Verify(r => r.GetActiveSessionsAsync(CafeId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActiveSessionsAsync_WithGameFilter_PassesGameId()
    {
        _posRepo.Setup(r => r.GetActiveSessionsAsync(CafeId, GameTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());

        var service = CreateService();

        var result = await service.GetActiveSessionsAsync(CafeId, ManagerId, "Manager", GameTemplateId);

        Assert.Empty(result);
        _posRepo.Verify(r => r.GetActiveSessionsAsync(CafeId, GameTemplateId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===================== EnsurePosAccessAsync (security gate) =====================

    [Fact]
    public async Task GetBoxesAsync_ManagerNoAccess_ThrowsForbidden()
    {
        // Mô phỏng user không có quyền trên cafe này
        _posRepo.Setup(r => r.CanOperateCafeAsync(CafeId, ManagerId, "Manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetBoxesAsync(CafeId, ManagerId, "Manager", null));
    }
}