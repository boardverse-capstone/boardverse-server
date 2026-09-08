using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

using System.Threading;
namespace BoardVerse.Tests.Services;

/// <summary>
/// Regression tests cho 4 gaps phát hiện trong audit ngày 2026-08-15:
/// <list type="bullet">
///   <item>GAP-A: 2 staff cùng submit component-check trên cùng session game
///                 → DB unique violation chuyển thành ConflictException (409)
///                 thay vì 500.</item>
///   <item>GAP-B+C: ResponsibleMemberId set cho dòng đủ component (no missing)
///                  → reject BadRequest thay vì silently drop.</item>
///   <item>GAP-D: GET /component-checklist yêu cầu session CHECKING giống
///                Submit/Reset — tránh FE mở nhầm UI khi session còn ACTIVE.</item>
/// </list>
/// </summary>
public class CafePosComponentCheckGapTests
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

    private static readonly Guid CafeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BoxId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SessionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GameTemplateId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid SessionGameId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ComponentAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ComponentBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MemberXId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GuestSlotId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CafeGameInventoryId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly Guid ManagerId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public CafePosComponentCheckGapTests()
    {
        _db = new FakeDbContext();
        _posRepo.Setup(r => r.CanOperateCafeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private static readonly MemoryCache MemoryCache = new(new MemoryCacheOptions());

    private CafePosService CreateService() => new(
        _posRepo.Object, _cafeRepo.Object, _depositRepo.Object, _bookingRepo.Object,
        _activeSessionRepo.Object, _activeSessionService.Object, _posHubService.Object,
        _lobbyRepo.Object, _userProfileRepo.Object, _reservationService.Object,
        _reservationRepo.Object, _tokenRepo.Object, MemoryCache, _logger.Object, _db);

    private static Cafe BuildCafe() => new()
    {
        Id = CafeId,
        ManagerId = ManagerId,
        Name = "Gap Test Cafe",
        Address = "456 Test Street",
        IsActive = true
    };

    private static GameTemplate BuildGameTemplate() => new()
    {
        Id = GameTemplateId,
        Name = "Catan",
        MaxPlayers = 4,
        MinPlayers = 3,
        IsActive = true,
        Components = new List<GameComponentTemplate>
        {
            new()
            {
                Id = ComponentAId,
                GameTemplateId = GameTemplateId,
                ComponentName = "Road tiles",
                DefaultQuantity = 15
            },
            new()
            {
                Id = ComponentBId,
                GameTemplateId = GameTemplateId,
                ComponentName = "Settlement pieces",
                DefaultQuantity = 20
            }
        }
    };

    private static ActiveSession BuildSession(
        Cafe cafe,
        GroupSessionStatus status = GroupSessionStatus.Checking,
        List<ActiveSessionMember>? members = null) => new()
    {
        Id = SessionId,
        CafeId = CafeId,
        Status = status,
        Cafe = cafe,
        Members = members ?? new List<ActiveSessionMember>()
    };

    private static ActiveSessionGame BuildSessionGame(GameTemplate gt) => new()
    {
        Id = SessionGameId,
        ActiveSessionId = SessionId,
        GameTemplateId = GameTemplateId,
        CafeInventoryBoxId = BoxId,
        CheckStatus = ComponentCheckStatus.NotChecked,
        GameTemplate = gt,
        CafeInventoryBox = new CafeInventoryBox
        {
            Id = BoxId,
            CafeGameInventoryId = CafeGameInventoryId,
            Barcode = "CATA-001",
            Status = CafeGameInventoryStatus.InUse
        }
    };

    // ============================================================
    // GAP-A: Concurrent submit → DbUpdateException với unique
    // violation chuyển thành ConflictException (409).
    // ============================================================

    [Fact]
    public async Task SubmitComponentCheck_ConcurrentDuplicate_ThrowsConflictException_NotInternalError()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var session = BuildSession(cafe);
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);

        // SaveChangesAsync ném DbUpdateException mô phỏng Postgres 23505 unique violation.
        _posRepo.Setup(r => r.AddComponentCheckResultsAsync(It.IsAny<List<ComponentCheckResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());
        _posRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "duplicate key value violates unique constraint \"IX_ComponentCheckResults_ActiveSessionGameId_GameComponentTemplateId\"",
                new MockPostgresUniqueViolationException()));

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = true,
                Results = new List<ComponentCheckResultItemDto>()
            }));

        Assert.Contains("Đã có người khác đang gửi checklist", ex.Message);
        // Concurrent message không embed session id — đó là message thân thiện cho staff,
        // ID đã có trong logs structured logging (không hiển thị cho user).
    }

    /// <summary>
    /// Mock exception kiểu Postgres: InnerException.Message chứa SQLSTATE 23505.
    /// </summary>
    private sealed class MockPostgresUniqueViolationException : Exception
    {
        public MockPostgresUniqueViolationException()
            : base("23505: duplicate key value violates unique constraint \"IX_test\"")
        {
        }
    }

    // ============================================================
    // GAP-B+C: ResponsibleMemberId cho dòng ĐỦ → BadRequest rõ ràng
    // (trước đây silently set về null ở cuối method).
    // ============================================================

    [Fact]
    public async Task SubmitComponentCheck_ResponsibleMemberIdForFullComponent_ThrowsBadRequest()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
var memberX = new ActiveSessionMember
            {
                Id = MemberXId,
                IsGuestSlot = false
            };
        var session = BuildSession(cafe, members: new List<ActiveSessionMember> { memberX });
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetComponentPenaltiesByCafeGameAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CafeGameComponentPenalty>());

        var service = CreateService();

        // Dòng A: đủ 15/15 nhưng set ResponsibleMemberId → phải bị reject.
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = false,
                Results = new List<ComponentCheckResultItemDto>
                {
                    new()
                    {
                        ComponentId = ComponentAId,
                        ActualQuantity = 15,  // ĐỦ
                        ResponsibleMemberId = MemberXId
                    },
                    new()
                    {
                        ComponentId = ComponentBId,
                        ActualQuantity = 18  // THIẾU 2, không set member → OK
                    }
                }
            }));

        Assert.Contains("khi chưa chọn linh kiện hỏng/mất cụ thể", ex.Message);
        Assert.Contains(MemberXId.ToString(), ex.Message);
    }

    [Fact]
    public async Task SubmitComponentCheck_ResponsibleMemberIdForMissingComponent_Accepted()
    {
        // Đảm bảo path cũ vẫn hoạt động: thiếu + có ResponsibleMemberId → accept (test regression).
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
var memberX = new ActiveSessionMember
            {
                Id = MemberXId,
                IsGuestSlot = false
            };
        var session = BuildSession(cafe, members: new List<ActiveSessionMember> { memberX });
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.AddComponentCheckResultsAsync(It.IsAny<List<ComponentCheckResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _posRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _posRepo.Setup(r => r.GetComponentPenaltiesByCafeGameAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CafeGameComponentPenalty>());
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());

        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = false,
                Results = new List<ComponentCheckResultItemDto>
                {
                    new()
                    {
                        ComponentId = ComponentAId,
                        ActualQuantity = 10,  // THIẾU 5
                        ResponsibleMemberId = MemberXId  // OK vì thiếu
                    },
                    new()
                    {
                        ComponentId = ComponentBId,
                        ActualQuantity = 20  // ĐỦ, không set member
                    }
                }
            }));

        Assert.Null(ex);
    }

    [Fact]
    public async Task SubmitComponentCheck_ResponsibleMemberIsGuestSlot_RejectedEvenIfMissing()
    {
        // Regression: BR-14 vẫn hoạt động đúng (cấm gán penalty cho Guest_Slot).
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var guestMember = new ActiveSessionMember
        {
            Id = GuestSlotId,
            IsGuestSlot = true  // <-- GUEST_SLOT
        };
        var session = BuildSession(cafe, members: new List<ActiveSessionMember> { guestMember });
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);

        // W-4: latestByComponent có thể null nếu repo không setup hoặc DB chưa có data.
        // Service hiện không defensive — test thêm setup trả empty dict để test pass.
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = false,
                Results = new List<ComponentCheckResultItemDto>
                {
                    new()
                    {
                        ComponentId = ComponentAId,
                        ActualQuantity = 5,  // THIẾU
                        ResponsibleMemberId = GuestSlotId  // BR-14 reject
                    },
                    new()
                    {
                        ComponentId = ComponentBId,
                        ActualQuantity = 10
                    }
                }
            }));

        Assert.Contains("vô danh", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================
    // GAP-D: GET /component-checklist yêu cầu session CHECKING.
    // ============================================================

    [Fact]
    public async Task GetComponentChecklist_SessionNotChecking_ThrowsConflictException()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var session = BuildSession(cafe, status: GroupSessionStatus.Active);  // <-- ACTIVE
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.GetComponentChecklistAsync(
            CafeId, ManagerId, "Manager", SessionGameId));

        Assert.Contains("Checking", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetComponentChecklist_SessionIsChecking_ReturnsChecklist()
    {
        // Smoke test: behavior cũ vẫn pass.
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var session = BuildSession(cafe, status: GroupSessionStatus.Checking);
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeGameComponentPenalty>());

        var service = CreateService();

        var dto = await service.GetComponentChecklistAsync(CafeId, ManagerId, "Manager", SessionGameId);
        Assert.Equal(2, dto.Components.Count);
        Assert.Empty(dto.OrphanedPenaltyItems);
    }

    // ============================================================
    // BR-BGG-SYNC-01: Orphaned penalties (component đã bị xóa khỏi BGG
    // nhưng penalty vẫn còn). Staff có thể gán penalty trên POS.
    // ============================================================

    [Fact]
    public async Task GetComponentChecklist_WithOrphanedPenalties_ReturnsOrphanedItems()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var session = BuildSession(cafe, status: GroupSessionStatus.Checking);
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        // 5 component đã bị xóa khỏi BGG nhưng penalty vẫn còn.
        var orphanedComponentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orphanedPenalties = new List<CafeGameComponentPenalty>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CafeGameInventoryId = CafeGameInventoryId,
                GameComponentTemplateId = orphanedComponentId,
                PenaltyFee = 15000m,
                GameComponentTemplate = new GameComponentTemplate
                {
                    Id = orphanedComponentId,
                    ComponentName = "Old Hex Tile",
                    GameTemplateId = GameTemplateId
                }
            }
        };

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orphanedPenalties);

        var service = CreateService();

        var dto = await service.GetComponentChecklistAsync(CafeId, ManagerId, "Manager", SessionGameId);

        // 2 component active vẫn trong checklist.
        Assert.Equal(2, dto.Components.Count);
        // 1 orphaned penalty được trả về.
        Assert.Single(dto.OrphanedPenaltyItems);
        Assert.Equal("Old Hex Tile", dto.OrphanedPenaltyItems[0].ComponentName);
        Assert.Equal(15000m, dto.OrphanedPenaltyItems[0].PenaltyFee);
    }

    [Fact]
    public async Task GetComponentChecklist_OrphanedPenalty_NoTemplateName_FallsBackToPlaceholder()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var session = BuildSession(cafe, status: GroupSessionStatus.Checking);
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        // Orphaned penalty với GameComponentTemplate = null (template đã bị xóa hoàn toàn).
        var orphanedPenalties = new List<CafeGameComponentPenalty>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CafeGameInventoryId = CafeGameInventoryId,
                GameComponentTemplateId = Guid.NewGuid(),
                PenaltyFee = 25000m,
                GameComponentTemplate = null!  // Template đã bị xóa
            }
        };

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orphanedPenalties);

        var service = CreateService();

        var dto = await service.GetComponentChecklistAsync(CafeId, ManagerId, "Manager", SessionGameId);

        Assert.Single(dto.OrphanedPenaltyItems);
        Assert.StartsWith("[Component ", dto.OrphanedPenaltyItems[0].ComponentName);
        Assert.Contains(orphanedPenalties[0].GameComponentTemplateId.ToString(), dto.OrphanedPenaltyItems[0].ComponentName);
    }

    [Fact]
    public async Task GetComponentChecklist_NoOrphanedPenalties_ReturnsEmptyOrphanedList()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var session = BuildSession(cafe, status: GroupSessionStatus.Checking);
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeGameComponentPenalty>());

        var service = CreateService();

        var dto = await service.GetComponentChecklistAsync(CafeId, ManagerId, "Manager", SessionGameId);

        Assert.Empty(dto.OrphanedPenaltyItems);
    }

    // ============================================================
    // BR-BGG-SYNC-01: Orphaned penalty submission (POST component-check)
    // ============================================================

    private static readonly Guid OrphanedPenaltyAId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid OrphanedPenaltyBId = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222");
    private static readonly Guid DeletedTemplateAId = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333");
    private static readonly Guid DeletedTemplateBId = Guid.Parse("aaaaaaaa-4444-4444-4444-444444444444");

    [Fact]
    public async Task SubmitComponentCheck_WithOrphanedPenalty_Valid_AddsPenaltyFeeAndMissingComponents()
    {
        // Arrange: Staff báo mất 1 linh kiện orphaned (component đã bị xóa khỏi BGG).
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var memberX = new ActiveSessionMember { Id = MemberXId, IsGuestSlot = false };
        var session = BuildSession(cafe, members: new List<ActiveSessionMember> { memberX });
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        var orphanedPenaltyA = new CafeGameComponentPenalty
        {
            Id = OrphanedPenaltyAId,
            GameComponentTemplateId = DeletedTemplateAId,
            PenaltyFee = 15000m
        };

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetComponentPenaltiesByCafeGameAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CafeGameComponentPenalty>());
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeGameComponentPenalty> { orphanedPenaltyA });
        _posRepo.Setup(r => r.AddComponentCheckResultsAsync(It.IsAny<List<ComponentCheckResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _posRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = false,
                Results = new List<ComponentCheckResultItemDto>
                {
                    new() { ComponentId = ComponentAId, ActualQuantity = 15 }, // đủ
                    new() { ComponentId = ComponentBId, ActualQuantity = 18 }  // thiếu 2
                },
                OrphanedPenaltyResults = new List<OrphanedPenaltyResultItemDto>
                {
                    new() { PenaltyId = OrphanedPenaltyAId, MissingQuantity = 1 }
                }
            });

        // Assert: Orphaned penalty được ghi nhận.
        Assert.Single(result.OrphanedPenaltyResults);
        Assert.Equal(OrphanedPenaltyAId, result.OrphanedPenaltyResults[0].PenaltyId);
        Assert.Equal(1, result.OrphanedPenaltyResults[0].MissingQuantity);
        Assert.Equal(15000m, result.OrphanedPenaltyResults[0].PenaltyFee);
        Assert.Equal(ComponentCheckStatus.MissingComponents, result.CheckStatus);
        // TotalPenalty = penalty orphaned (15000) + penalty ComponentB (2 × penaltyBFee, hoặc 0 nếu không có config)
        Assert.True(result.TotalPenaltyAmount > 0);
    }

    [Fact]
    public async Task SubmitComponentCheck_WithOrphanedPenalty_PenaltyNotFound_ThrowsBadRequest()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var memberX = new ActiveSessionMember { Id = MemberXId, IsGuestSlot = false };
        var session = BuildSession(cafe, members: new List<ActiveSessionMember> { memberX });
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            // Không có orphaned penalty nào → PenaltyId không hợp lệ
            .ReturnsAsync(new List<CafeGameComponentPenalty>());

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = false,
                Results = new List<ComponentCheckResultItemDto>
                {
                    new() { ComponentId = ComponentAId, ActualQuantity = 15 },
                    new() { ComponentId = ComponentBId, ActualQuantity = 20 }
                },
                OrphanedPenaltyResults = new List<OrphanedPenaltyResultItemDto>
                {
                    new() { PenaltyId = OrphanedPenaltyAId, MissingQuantity = 1 }
                }
            }));

        Assert.Contains("Không tìm thấy cấu hình phí đền bù", ex.Message);
    }

    [Fact]
    public async Task SubmitComponentCheck_WithOrphanedPenalty_GuestSlotResponsible_ThrowsBadRequest()
    {
        // BR-14: Không được gán penalty cho Guest_Slot.
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var memberX = new ActiveSessionMember { Id = MemberXId, IsGuestSlot = false };
        var guestSlot = new ActiveSessionMember { Id = GuestSlotId, IsGuestSlot = true };
        var session = BuildSession(cafe, members: new List<ActiveSessionMember> { memberX, guestSlot });
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        var orphanedPenaltyA = new CafeGameComponentPenalty
        {
            Id = OrphanedPenaltyAId,
            GameComponentTemplateId = DeletedTemplateAId,
            PenaltyFee = 15000m
        };

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeGameComponentPenalty> { orphanedPenaltyA });

        var service = CreateService();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = false,
                Results = new List<ComponentCheckResultItemDto>
                {
                    new() { ComponentId = ComponentAId, ActualQuantity = 15 },
                    new() { ComponentId = ComponentBId, ActualQuantity = 20 }
                },
                OrphanedPenaltyResults = new List<OrphanedPenaltyResultItemDto>
                {
                    new() { PenaltyId = OrphanedPenaltyAId, MissingQuantity = 1, ResponsibleMemberId = GuestSlotId }
                }
            }));

        Assert.Contains("khách vô danh", ex.Message);
    }

    [Fact]
    public async Task SubmitComponentCheck_WithOrphanedPenalty_MemberNotInSession_ThrowsBadRequest()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var memberX = new ActiveSessionMember { Id = MemberXId, IsGuestSlot = false };
        var session = BuildSession(cafe, members: new List<ActiveSessionMember> { memberX });
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        var orphanedPenaltyA = new CafeGameComponentPenalty
        {
            Id = OrphanedPenaltyAId,
            GameComponentTemplateId = DeletedTemplateAId,
            PenaltyFee = 15000m
        };

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeGameComponentPenalty> { orphanedPenaltyA });

        var service = CreateService();
        var nonExistentMemberId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = false,
                Results = new List<ComponentCheckResultItemDto>
                {
                    new() { ComponentId = ComponentAId, ActualQuantity = 15 },
                    new() { ComponentId = ComponentBId, ActualQuantity = 20 }
                },
                OrphanedPenaltyResults = new List<OrphanedPenaltyResultItemDto>
                {
                    new() { PenaltyId = OrphanedPenaltyAId, MissingQuantity = 1, ResponsibleMemberId = nonExistentMemberId }
                }
            }));

        Assert.Contains("Không thể gán phí đền bù", ex.Message);
    }

    [Fact]
    public async Task SubmitComponentCheck_WithOrphanedPenalty_MarkAllValid_StillProcessesOrphaned()
    {
        // MarkAllValid = true nhưng vẫn gửi orphaned penalty → orphaned vẫn được xử lý.
        var cafe = BuildCafe();
        var gt = BuildGameTemplate();
        var memberX = new ActiveSessionMember { Id = MemberXId, IsGuestSlot = false };
        var session = BuildSession(cafe, members: new List<ActiveSessionMember> { memberX });
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        var orphanedPenaltyA = new CafeGameComponentPenalty
        {
            Id = OrphanedPenaltyAId,
            GameComponentTemplateId = DeletedTemplateAId,
            PenaltyFee = 20000m
        };

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ComponentCheckResult>());
        _posRepo.Setup(r => r.GetOrphanedPenaltiesAsync(CafeGameInventoryId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeGameComponentPenalty> { orphanedPenaltyA });
        _posRepo.Setup(r => r.AddComponentCheckResultsAsync(It.IsAny<List<ComponentCheckResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _posRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = true,
                Results = new List<ComponentCheckResultItemDto>(), // MarkAllValid → không cần gửi components
                OrphanedPenaltyResults = new List<OrphanedPenaltyResultItemDto>
                {
                    new() { PenaltyId = OrphanedPenaltyAId, MissingQuantity = 1 }
                }
            });

        // Assert: Orphaned penalty được ghi nhận, box vào Maintenance.
        Assert.Single(result.OrphanedPenaltyResults);
        Assert.Equal(20000m, result.OrphanedPenaltyResults[0].PenaltyFee);
        Assert.Equal(20000m, result.TotalPenaltyAmount);
        Assert.Equal(ComponentCheckStatus.MissingComponents, result.CheckStatus);
    }
}
