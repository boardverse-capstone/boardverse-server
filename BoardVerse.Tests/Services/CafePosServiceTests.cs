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
        // Default: cafe tồn tại và active.
        // GAP-FIX-DataBlank-Manager (2026-09-29): Manager flow dùng GetByIdAsync (không filter IsActive),
        // CafeStaff flow dùng GetActiveByIdAsync (filter IsActive). Mock cả 2 để các test cũ không vỡ.
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe());
        _cafeRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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

    // ===================== SyncTablesAsync =====================
    // Verify rằng service forward đúng Name + SeatCount + SortOrder xuống repository
    // để CafeTableSyncHelper tạo/cập nhật bàn. Service KHÔNG tự auto-fill SortOrder theo
    // index — đó là trách nhiệm của helper.

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

    [Fact]
    public async Task SyncTablesAsync_TablesItems_PreservesSeatCountAndSortOrder()
    {
        _cafeRepo.Setup(r => r.SyncCafeTablesAsync(CafeId, It.IsAny<IReadOnlyList<CafeTableSyncItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var payload = new[]
        {
            new CafeTableSyncItem { Name = "Bàn 1", SeatCount = 4,  SortOrder = 0 },
            new CafeTableSyncItem { Name = "Bàn 2", SeatCount = 4,  SortOrder = 1 },
            new CafeTableSyncItem { Name = "Bàn 3", SeatCount = 4,  SortOrder = 2 },
            new CafeTableSyncItem { Name = "Bàn 4", SeatCount = 4,  SortOrder = 3 },
            new CafeTableSyncItem { Name = "Bàn 5", SeatCount = 4,  SortOrder = 4 },
            new CafeTableSyncItem { Name = "Bàn 6", SeatCount = 4,  SortOrder = 5 },
            new CafeTableSyncItem { Name = "Bàn 7", SeatCount = 4,  SortOrder = 6 },
            new CafeTableSyncItem { Name = "Bàn 8", SeatCount = 4,  SortOrder = 7 },
            new CafeTableSyncItem { Name = "Bàn 9", SeatCount = 4,  SortOrder = 8 },
            new CafeTableSyncItem { Name = "Bàn 10", SeatCount = 4, SortOrder = 9 }
        };

        await service.SyncTablesAsync(CafeId, ManagerId, payload);

        _cafeRepo.Verify(r => r.SyncCafeTablesAsync(
            CafeId,
            It.Is<IReadOnlyList<CafeTableSyncItem>>(items =>
                items.Count == 10
                && items[0].Name == "Bàn 1"  && items[0].SeatCount == 4  && items[0].SortOrder == 0
                && items[9].Name == "Bàn 10" && items[9].SeatCount == 4  && items[9].SortOrder == 9),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncTablesAsync_TablesItems_MixedSeatCount_PassesThrough()
    {
        // Đảm bảo service forward nguyên SeatCount + SortOrder mà caller truyền vào.
        _cafeRepo.Setup(r => r.SyncCafeTablesAsync(CafeId, It.IsAny<IReadOnlyList<CafeTableSyncItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var payload = new[]
        {
            new CafeTableSyncItem { Name = "Bàn VIP",    SeatCount = 12, SortOrder = 0 },
            new CafeTableSyncItem { Name = "Bàn thường", SeatCount = 4,  SortOrder = 5 }
        };

        await service.SyncTablesAsync(CafeId, ManagerId, payload);

        _cafeRepo.Verify(r => r.SyncCafeTablesAsync(
            CafeId,
            It.Is<IReadOnlyList<CafeTableSyncItem>>(items =>
                items.Count == 2
                && items[0].Name == "Bàn VIP"    && items[0].SeatCount == 12 && items[0].SortOrder == 0
                && items[1].Name == "Bàn thường" && items[1].SeatCount == 4  && items[1].SortOrder == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncTablesAsync_TablesItems_NullSeatCount_ForwardedAsNull()
    {
        // Service KHÔNG độn default 4 — đó là trách nhiệm của helper. Service chỉ forward payload.
        _cafeRepo.Setup(r => r.SyncCafeTablesAsync(CafeId, It.IsAny<IReadOnlyList<CafeTableSyncItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var payload = new[]
        {
            new CafeTableSyncItem { Name = "Bàn A", SeatCount = null, SortOrder = 0 }
        };

        await service.SyncTablesAsync(CafeId, ManagerId, payload);

        _cafeRepo.Verify(r => r.SyncCafeTablesAsync(
            CafeId,
            It.Is<IReadOnlyList<CafeTableSyncItem>>(items =>
                items.Count == 1
                && items[0].Name == "Bàn A"
                && items[0].SeatCount == null
                && items[0].SortOrder == 0),
            It.IsAny<CancellationToken>()), Times.Once);
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

    // GAP-FIX-DataBlank-Manager (2026-09-29):
    // Manager sở hữu cafe phải truy cập được POS ở trạng thái DataBlank (IsActive=false)
    // để sync bàn + sửa profile trước khi activate. Trước đây EnsurePosAccessAsync dùng
    // GetActiveByIdAsync (filter IsActive=true) + CanOperateCafeAsync cũng filter IsActive
    // → Manager không thể sync bàn ở DataBlank → deadlock với requirement ≥5 bàn.
    [Fact]
    public async Task EnsurePosAccessAsync_ManagerDataBlankCafe_DoesNotThrowNotFound()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var dataBlankCafe = new Cafe
        {
            Id = cafeId,
            ManagerId = managerId,
            Name = "DataBlank Cafe",
            Address = "test",
            IsActive = false,                                          // ← DataBlank: IsActive = false
            PartnerOperationalStatus = CafePartnerOperationalStatus.DataBlank
        };

        // Manager flow dùng GetByIdAsync (không filter IsActive) — phải trả cafe.
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataBlankCafe);
        // GetActiveByIdAsync trả null vì cafe không ACTIVE.
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);
        _posRepo.Setup(r => r.CanOperateCafeAsync(cafeId, managerId, "Manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // GetBoxesAsync lookup sau khi qua gate.
        _posRepo.Setup(r => r.GetBoxesAsync(cafeId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeInventoryBox>());

        var service = CreateService();

        // Trước fix: throws NotFoundException("Không tìm thấy quán...")
        // Sau fix: pass qua gate, gọi thẳng vào POS logic.
        var result = await service.GetBoxesAsync(cafeId, managerId, "Manager", null);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task EnsurePosAccessAsync_CafeStaffDataBlankCafe_ThrowsNotFound()
    {
        // CafeStaff vẫn bị chặn ở DataBlank — staff không có lý do vào POS cafe đang cấu hình.
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);  // CafeStaff dùng GetActiveByIdAsync → null vì DataBlank

        var service = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetBoxesAsync(cafeId, staffId, "CafeStaff", null));
    }
}