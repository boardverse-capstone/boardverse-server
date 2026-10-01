using System.Reflection;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// M1 / Phase 4a — Unit tests cho <c>ActiveSessionService.BuildMemberInvoices</c>
/// (Host Deposit Discount logic).
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §B2 + §B5.
///
/// <para>
/// <b>Test approach:</b> <c>BuildMemberInvoices</c> là private helper. Mình dùng reflection
/// (theo pattern LobbyMemberCleanupTests / ReservationSerializationFailureDetectionTests) để
/// gọi trực tiếp với crafted entities — tránh mock toàn bộ 16 dependencies của
/// ActiveSessionService constructor.
/// </para>
/// </summary>
public class M1HostDepositDiscountTests : IDisposable
{
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<IActiveSessionRepository> _activeSessionRepo = new();
    private readonly Mock<ICafePosRepository> _posRepo = new();
    private readonly Mock<IBookingDepositRepository> _depositRepo = new();
    private readonly Mock<ISettlementService> _settlementService = new();
    private readonly Mock<IReservationService> _reservationService = new();
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<IWalkInService> _walkInService = new();
    private readonly Mock<IOutboxRepository> _outboxRepo = new();
    private readonly Mock<ILogger<ActiveSessionService>> _logger = new();

    private BoardVerseDbContext _db = default!;

    public M1HostDepositDiscountTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new BoardVerseDbContext(options);
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    /// <summary>
    /// Build ActiveSessionService với mocked dependencies (chỉ cần đủ để khởi tạo).
    /// </summary>
    private ActiveSessionService CreateService() => new(
        _cafeRepo.Object, _activeSessionRepo.Object, _posRepo.Object,
        _depositRepo.Object, _settlementService.Object, _reservationService.Object,
        _lobbyRepo.Object, _reservationRepo.Object, _walkInService.Object,
        _outboxRepo.Object, _logger.Object);

    /// <summary>
    /// Invoke private BuildMemberInvoices qua reflection (match project pattern).
    /// </summary>
    private static List<MemberInvoiceDto> InvokeBuildMemberInvoices(
        ActiveSessionService service,
        ActiveSession session,
        Cafe cafe,
        List<ComponentCheckResult> componentCheckResults,
        List<ComponentPenaltyItemDto>? legacyPenaltyItems,
        HostDepositUsageMode hostDepositUsage,
        Reservation? reservation,
        DateTime payTime)
    {
        var method = typeof(ActiveSessionService).GetMethod(
            "BuildMemberInvoices",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildMemberInvoices method not found.");

        var result = method.Invoke(service, new object?[]
        {
            session, cafe, componentCheckResults, legacyPenaltyItems,
            hostDepositUsage, reservation, payTime
        }) as List<MemberInvoiceDto>;

        return result ?? new List<MemberInvoiceDto>();
    }

    /// <summary>
    /// Helper tạo ActiveSession với N members theo spec.
    /// </summary>
    private static ActiveSession CreateSessionWithMembers(
        Guid sessionId,
        Guid cafeId,
        IEnumerable<(Guid userId, bool isHost, int minutes, DateTime? leftAt)> memberSpecs,
        decimal subtotal = 0m,
        decimal penalty = 0m)
    {
        var members = memberSpecs.Select(spec => new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = sessionId,
            UserId = spec.userId,
            IsHost = spec.isHost,
            IsGuestSlot = false,
            Status = IndividualSessionStatus.Playing,
            JoinedAt = DateTime.UtcNow.AddHours(-2),
            TotalMinutesPlayed = spec.minutes,
            LeftAt = spec.leftAt,
            DepositId = null,
            DepositAppliedAmount = 0m
        }).ToList();

        return new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            LobbyId = Guid.NewGuid(),
            Status = GroupSessionStatus.Unpaid,
            Subtotal = subtotal,
            PenaltyAmount = penalty,
            Members = members
        };
    }

    private static Cafe CreateCafe(Guid cafeId) => new()
    {
        Id = cafeId,
        Name = "Test Cafe",
        Address = "123 Test St",
        IsActive = true,
        PartnerOperationalStatus = CafePartnerOperationalStatus.Active,
        BillingModel = CafePartnerBillingModel.TimeBased,
        BasePrice = 50000m, // 50k VND/giờ đầu - đảm bảo member bill > 0
        TieredBlockRate = 30000m,
        TieredBlockMinutes = 30
    };

    private static Reservation CreateReservation(
        Guid reservationId,
        Guid cafeId,
        Guid hostId,
        long depositAmount,
        long carriedOverBvc = 0,
        ReservationStatus status = ReservationStatus.CheckedIn,
        bool sourceDissolved = false)
    {
        return new Reservation
        {
            Id = reservationId,
            CafeId = cafeId,
            HostId = hostId,
            DepositAmount = depositAmount,
            CarriedOverDepositBvc = carriedOverBvc,
            Status = status,
            SourceDissolved = sourceDissolved
        };
    }

    // =====================================================================
    // CASE 1: DiscountGroup mode
    // =====================================================================

    /// <summary>
    /// DiscountGroup: 4 members (60 min each) chia đều pool 50k → 12500 mỗi member.
    /// M1 acceptance criteria: "deposit được phân bổ theo minutes cho active members".
    /// Setup: subtotal = 50000 = deposit → pool không bị cap, mỗi member nhận đúng 12500.
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_DiscountGroup_DividesPoolProportionalToMinutes()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null),         // Host
            (Guid.NewGuid(), false, 60, (DateTime?)null), // Member B
            (Guid.NewGuid(), false, 60, (DateTime?)null), // Member C
            (Guid.NewGuid(), false, 60, (DateTime?)null)  // Member D
        }, subtotal: 50000m, penalty: 0m); // session total = 50k = deposit → no cap
        var reservation = CreateReservation(reservationId, cafeId, hostId, depositAmount: 50000);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(),
            legacyPenaltyItems: null,
            hostDepositUsage: HostDepositUsageMode.DiscountGroup,
            reservation: reservation, payTime: DateTime.UtcNow);

        // 4 members → mỗi member = 50000 * 60/240 = 12500
        Assert.Equal(4, invoices.Count);
        Assert.All(invoices, inv => Assert.Equal(12500L, inv.DiscountAppliedAmount));

        // Total discount applied = 4 × 12500 = 50000 (full pool, no remainder)
        var totalDiscount = invoices.Sum(i => i.DiscountAppliedAmount);
        Assert.Equal(50000L, totalDiscount);
    }

    /// <summary>
    /// DiscountGroup: member có TotalMinutesPlayed = 0 bị EXCLUDED (không nhận discount).
    /// Verify logic: line 1604-1611 chỉ include members có TotalMinutesPlayed > 0.
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_DiscountGroup_ExcludesMembersWithZeroMinutes()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null),          // active 60 min
            (Guid.NewGuid(), false, 60, (DateTime?)null), // active 60 min
            (Guid.NewGuid(), false, 0, (DateTime?)null),  // 0 min - EXCLUDED
            (Guid.NewGuid(), false, 60, (DateTime?)null)  // active 60 min
        }, subtotal: 30000m);
        var reservation = CreateReservation(reservationId, cafeId, hostId, depositAmount: 30000);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(), null,
            HostDepositUsageMode.DiscountGroup, reservation, DateTime.UtcNow);

        Assert.Equal(4, invoices.Count);
        // 3 active × 60 min total = 180 min → 10000 mỗi member (3 × 10000 = 30000)
        var activeInvoices = invoices.Where(i => i.DiscountAppliedAmount > 0).ToList();
        Assert.Equal(3, activeInvoices.Count);
        Assert.All(activeInvoices, inv => Assert.Equal(10000L, inv.DiscountAppliedAmount));

        var zeroMinInvoice = invoices.First(i => i.PlayedMinutes == 0);
        Assert.Equal(0L, zeroMinInvoice.DiscountAppliedAmount);
    }

    /// <summary>
    /// DiscountGroup: A3 rời sớm (LeftAt &lt; payTime) → EXCLUDED khỏi discount pool.
    /// M1 / Exception 4 acceptance: "A3 đã merge → EXCLUDED khỏi Group A's discount".
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_DiscountGroup_ExcludesMembersWhoLeftBeforePayTime()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var a3UserId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var now = DateTime.UtcNow;
        var leftAt = now.AddMinutes(-5); // A3 left 5 min ago
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null),          // active
            (Guid.NewGuid(), false, 60, (DateTime?)null), // active
            (a3UserId, false, 30, leftAt)                 // A3 LEFT
        }, subtotal: 30000m);
        var reservation = CreateReservation(reservationId, cafeId, hostId, depositAmount: 30000);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(), null,
            HostDepositUsageMode.DiscountGroup, reservation, payTime: now);

        // A3's LeftAt = now-5min &lt; payTime(now) → excluded
        var a3Invoice = invoices.First(i => i.UserId == a3UserId);
        Assert.Equal(0L, a3Invoice.DiscountAppliedAmount);

        // Host + B nhận full 15000 mỗi (30000 / 2 active × 60 min)
        var activeInvoices = invoices.Where(i => i.DiscountAppliedAmount > 0).ToList();
        Assert.Equal(2, activeInvoices.Count);
        Assert.All(activeInvoices, inv => Assert.Equal(15000L, inv.DiscountAppliedAmount));
    }

    // =====================================================================
    // CASE 2: DiscountHostOnly mode
    // =====================================================================

    /// <summary>
    /// DiscountHostOnly: chỉ host được discount, các members khác = 0.
    /// M1 acceptance: "deposit giảm bill của host".
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_DiscountHostOnly_AppliesDiscountOnlyToHost()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null),          // Host
            (Guid.NewGuid(), false, 60, (DateTime?)null),
            (Guid.NewGuid(), false, 60, (DateTime?)null)
        }, subtotal: 30000m);
        var reservation = CreateReservation(reservationId, cafeId, hostId, depositAmount: 50000);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(), null,
            HostDepositUsageMode.DiscountHostOnly, reservation, DateTime.UtcNow);

        var hostInvoice = invoices.First(i => i.UserId == hostId);
        // pool = min(50k deposit, 30k subtotal) = 30k → full discount cho host
        Assert.Equal(30000L, hostInvoice.DiscountAppliedAmount);

        // Other members = 0
        var otherInvoices = invoices.Where(i => i.UserId != hostId).ToList();
        Assert.All(otherInvoices, inv => Assert.Equal(0L, inv.DiscountAppliedAmount));
    }

    // =====================================================================
    // CASE 3: None mode (default - BR-09 cũ)
    // =====================================================================

    /// <summary>
    /// HostDepositUsageMode.None: không có discount nào apply.
    /// M1 acceptance: "POS dropdown chọn None" → fallback BR-09 cũ.
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_NoneMode_NoDiscountApplied()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null),
            (Guid.NewGuid(), false, 60, (DateTime?)null)
        }, subtotal: 30000m);
        var reservation = CreateReservation(reservationId, cafeId, hostId, depositAmount: 50000);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(), null,
            HostDepositUsageMode.None, reservation, DateTime.UtcNow);

        Assert.All(invoices, inv => Assert.Equal(0L, inv.DiscountAppliedAmount));
    }

    // =====================================================================
    // CASE 4: Reservation eligibility check
    // =====================================================================

    /// <summary>
    /// Reservation status = Cancelled → skip discount (fallback None).
    /// M1 acceptance: "Reservation status whitelist (Holding/Confirmed/CheckedIn)".
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_CancelledReservation_SkipsDiscount()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null)
        }, subtotal: 30000m);
        var reservation = CreateReservation(
            reservationId, cafeId, hostId,
            depositAmount: 50000,
            status: ReservationStatus.CancelledByPlayer);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(), null,
            HostDepositUsageMode.DiscountGroup, reservation, DateTime.UtcNow);

        // Cancelled reservation → IsReservationEligibleForDiscount returns false
        Assert.All(invoices, inv => Assert.Equal(0L, inv.DiscountAppliedAmount));
    }

    /// <summary>
    /// SourceDissolved=true (reservation đã absorbed by merge) → skip discount.
    /// Verify edge case: source reservation sau khi dissolve không được apply discount
    /// tại Group A's Pay (vì deposit đã transferred sang target).
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_SourceDissolved_SkipsDiscount()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null)
        }, subtotal: 30000m);
        var reservation = CreateReservation(
            reservationId, cafeId, hostId,
            depositAmount: 50000,
            status: ReservationStatus.AbsorbedByMerge,
            sourceDissolved: true);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(), null,
            HostDepositUsageMode.DiscountGroup, reservation, DateTime.UtcNow);

        Assert.All(invoices, inv => Assert.Equal(0L, inv.DiscountAppliedAmount));
    }

    // =====================================================================
    // CASE 5: Exception 4 / Host Deposit Follows merge chain
    // =====================================================================

    /// <summary>
    /// M1 / Exception 4 acceptance: "Multi-hop merge chain (A → B → C): carried-over
    /// accumulates correctly". Verify: target reservation có CarriedOverDepositBvc từ
    /// source merge → discount pool = DepositAmount + CarriedOverDepositBvc.
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_MergedFromLobby_CarriedOverDepositCountedInEffectiveDeposit()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null),          // Host B (sau khi A merge vào)
            (Guid.NewGuid(), false, 60, (DateTime?)null)  // Merged member from source A
        }, subtotal: 40000m);
        // Reservation B có DepositAmount=0 (host B ko trả) + CarriedOverDepositBvc=100000 (A's deposit)
        var reservation = CreateReservation(
            reservationId, cafeId, hostId,
            depositAmount: 0,
            carriedOverBvc: 100000,
            status: ReservationStatus.CheckedIn);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(), null,
            HostDepositUsageMode.DiscountGroup, reservation, DateTime.UtcNow);

        // Effective pool = max(0, subtotal=40k) = 40k (clamped theo session total)
        // → chia đều 2 members = 20000 mỗi member
        Assert.All(invoices, inv => Assert.Equal(20000L, inv.DiscountAppliedAmount));
        var totalDiscount = invoices.Sum(i => i.DiscountAppliedAmount);
        Assert.Equal(40000L, totalDiscount);
    }

    /// <summary>
    /// Remainder test: deposit 50k > session total 30k → chỉ apply 30k, remainder = 17k.
    /// Verify pool = min(DepositAmount, sessionTotal) (BR-15 modified).
    /// </summary>
    [Fact]
    public void BuildMemberInvoices_DepositExceedsSessionTotal_CapsAtSessionTotal()
    {
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = CreateCafe(cafeId);
        var session = CreateSessionWithMembers(sessionId, cafeId, new[]
        {
            (hostId, true, 60, (DateTime?)null),
            (Guid.NewGuid(), false, 60, (DateTime?)null)
        }, subtotal: 30000m);
        var reservation = CreateReservation(reservationId, cafeId, hostId, depositAmount: 50000);

        var invoices = InvokeBuildMemberInvoices(
            CreateService(), session, cafe, new List<ComponentCheckResult>(), null,
            HostDepositUsageMode.DiscountGroup, reservation, DateTime.UtcNow);

        // Pool = min(50000, 30000) = 30000 → 15000 mỗi member
        Assert.All(invoices, inv => Assert.Equal(15000L, inv.DiscountAppliedAmount));
        var totalDiscount = invoices.Sum(i => i.DiscountAppliedAmount);
        // Tổng discount = 30000 (pool bị cap tại subtotal)
        Assert.Equal(30000L, totalDiscount);
    }
}
