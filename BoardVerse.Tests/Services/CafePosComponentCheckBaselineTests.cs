using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Regression test cho bug "ComponentCheck baseline = 0 throws ArgumentException":
///
/// Trước fix: khi box từng được check với ActualQuantity = 0 (staff nhập mất hết),
/// lần check sau vẫn lấy baseline cũ = 0 → setter ExpectedQuantity throw "Số lượng
/// mặc định phải lớn hơn 0" → API trả 500.
///
/// Sau fix: nếu baseline cũ &lt;= 0, fallback về <c>DefaultQuantity</c> từ template.
/// </summary>
public class CafePosComponentCheckBaselineTests
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

    private static readonly Guid CafeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BoxId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GameTemplateId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SessionGameId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ComponentId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ManagerId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public CafePosComponentCheckBaselineTests()
    {
        _db = new FakeDbContext();
        _posRepo.Setup(r => r.CanOperateCafeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
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
        _logger.Object,
        _db);

    private static Cafe BuildCafe() => new()
    {
        Id = CafeId,
        ManagerId = ManagerId,
        Name = "Baseline Test Cafe",
        Address = "123 Test Street",
        IsActive = true
    };

    private static GameTemplate BuildGameTemplate(int defaultQuantity) => new()
    {
        Id = GameTemplateId,
        Name = "Ticket to Ride",
        MaxPlayers = 4,
        MinPlayers = 2,
        IsActive = true,
        Components = new List<GameComponentTemplate>
        {
            new()
            {
                Id = ComponentId,
                GameTemplateId = GameTemplateId,
                ComponentName = "Train Cards",
                DefaultQuantity = defaultQuantity
            }
        }
    };

    private static ActiveSessionGame BuildSessionGame(GameTemplate gt) => new()
    {
        Id = SessionGameId,
        ActiveSessionId = SessionId,
        GameTemplateId = GameTemplateId,
        CafeInventoryBoxId = BoxId,
        CheckStatus = ComponentCheckStatus.NotChecked,
        GameTemplate = gt
    };

    private static ActiveSession BuildSession(Cafe cafe) => new()
    {
        Id = SessionId,
        CafeId = CafeId,
        Status = GroupSessionStatus.Checking,
        Cafe = cafe,
        Members = new List<ActiveSessionMember>()
    };

    /// <summary>
    /// Baseline cũ = 0 (box từng mất hết linh kiện ở phiên trước)
    /// → fallback <c>DefaultQuantity = 240</c> → component-check KHÔNG throw.
    /// Đây chính là bug production mà stack trace ghi rõ.
    /// </summary>
    [Fact]
    public async Task GetComponentChecklist_BaselineIsZero_FallsBackToDefaultQuantity()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate(defaultQuantity: 240);
        var session = BuildSession(cafe);
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id)).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId))
            .ReturnsAsync(sessionGame);

        // Baseline cũ: ActualQuantity = 0 (staff nhập mất hết ở phiên trước).
        var staleBaseline = new List<ComponentCheckResult>
        {
            new()
            {
                ActiveSessionGameId = Guid.NewGuid(),
                GameComponentTemplateId = ComponentId,
                ActualQuantity = 0,           // <-- bug input
                ExpectedQuantity = 1,
                CheckedAt = DateTime.UtcNow.AddDays(-3)
            }
        };
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId))
            .ReturnsAsync(staleBaseline
                .GroupBy(x => x.GameComponentTemplateId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CheckedAt).First()));

        var service = CreateService();

        var result = await service.GetComponentChecklistAsync(
            CafeId, ManagerId, "Manager", SessionGameId);

        Assert.NotNull(result);
        Assert.Single(result.Components);
        Assert.Equal(240, result.Components[0].ExpectedQuantity);
        Assert.Equal("Train Cards", result.Components[0].ComponentName);
    }

    /// <summary>
    /// markAllValid path: baseline = 0 → vẫn phải không throw ArgumentException.
    /// </summary>
    [Fact]
    public async Task SubmitComponentCheck_MarkAllValid_BaselineIsZero_FallsBackToDefaultQuantity()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate(defaultQuantity: 45);
        var session = BuildSession(cafe);
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id)).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.AddComponentCheckResultsAsync(It.IsAny<List<ComponentCheckResult>>()))
            .Returns(Task.CompletedTask);
        _posRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var staleBaseline = new List<ComponentCheckResult>
        {
            new()
            {
                ActiveSessionGameId = Guid.NewGuid(),
                GameComponentTemplateId = ComponentId,
                ActualQuantity = 0,
                ExpectedQuantity = 1,
                CheckedAt = DateTime.UtcNow.AddDays(-3)
            }
        };
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId))
            .ReturnsAsync(staleBaseline
                .GroupBy(x => x.GameComponentTemplateId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CheckedAt).First()));

        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = true,
                Results = new List<ComponentCheckResultItemDto>()
            }));

        Assert.Null(ex);
    }

    /// <summary>
    /// Chi tiết path (results != []): baseline = 0 vẫn không throw.
    /// Đây là đường gốc trong stack trace 500 trên production.
    /// </summary>
    [Fact]
    public async Task SubmitComponentCheck_DetailedMode_BaselineIsZero_FallsBackToDefaultQuantity()
    {
        var cafe = BuildCafe();
        var gt = BuildGameTemplate(defaultQuantity: 100);
        var session = BuildSession(cafe);
        var sessionGame = BuildSessionGame(gt);
        sessionGame.ActiveSession = session;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id)).ReturnsAsync(cafe);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetActiveSessionGameByIdAsync(SessionGameId))
            .ReturnsAsync(sessionGame);
        _posRepo.Setup(r => r.GetComponentPenaltiesByCafeGameAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, CafeGameComponentPenalty>());
        _posRepo.Setup(r => r.AddComponentCheckResultsAsync(It.IsAny<List<ComponentCheckResult>>()))
            .Returns(Task.CompletedTask);
        _posRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var staleBaseline = new List<ComponentCheckResult>
        {
            new()
            {
                ActiveSessionGameId = Guid.NewGuid(),
                GameComponentTemplateId = ComponentId,
                ActualQuantity = 0,
                ExpectedQuantity = 1,
                CheckedAt = DateTime.UtcNow.AddDays(-3)
            }
        };
        _posRepo.Setup(r => r.GetLatestComponentCheckByBoxAsync(BoxId))
            .ReturnsAsync(staleBaseline
                .GroupBy(x => x.GameComponentTemplateId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CheckedAt).First()));

        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.SubmitComponentCheckAsync(
            CafeId, ManagerId, "Manager",
            new SubmitComponentCheckRequestDto
            {
                SessionGameId = SessionGameId,
                MarkAllValid = false,
                Results = new List<ComponentCheckResultItemDto>
                {
                    new() { ComponentId = ComponentId, ActualQuantity = 100 }
                }
            }));

        Assert.Null(ex);
    }
}
