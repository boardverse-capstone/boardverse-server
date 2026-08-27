using BoardVerse.Core.DTOs.Session;
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

/// <summary>
/// Tests cho GAP-3 (Timezone), GAP-8 (History filter), GAP-11 (Invoice breakdown), GAP-13 (LeftAt).
/// </summary>
public class PlayerSessionGapsTests
{
    private static User CreateUser(Guid id, string username = "player1") => new()
    {
        Id = id,
        Username = username,
        Email = $"{username}@test.local"
    };

    private static List<ActiveSessionMember> CreateMember(Guid userId, IndividualSessionStatus status, DateTime joinedAt, DateTime? leftAt = null, int minutes = 0)
    {
        return new List<ActiveSessionMember>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = status,
                JoinedAt = joinedAt,
                LeftAt = leftAt,
                TotalMinutesPlayed = minutes,
                Subtotal = 60000m,
                PenaltyAmount = 0m,
                DepositAppliedAmount = 0m,
                User = CreateUser(userId)
            }
        };
    }

    private static ActiveSession CreatePaidSession(Guid userId, Guid cafeId, DateTime joinedAt, DateTime paidAt)
    {
        var memberList = CreateMember(userId, IndividualSessionStatus.Finished, joinedAt, paidAt, 60);
        return new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = userId,
            Status = GroupSessionStatus.Paid,
            StartedAt = joinedAt,
            PaidAt = paidAt,
            Subtotal = 60000m,
            TotalAmount = 60000m,
            LobbyId = Guid.NewGuid(),
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan" },
            Games = new List<ActiveSessionGame>
            {
                new() { GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan" } }
            },
            Members = memberList,
            Cafe = new Cafe { Id = cafeId, Name = "Test Cafe", Address = "123 St", IsActive = true }
        };
    }

    /// <summary>
    /// Tạo service với deps chuẩn. extRepo/wallet có thể override.
    /// </summary>
    private static (ActiveSessionService svc, Mock<ISessionExtensionRequestRepository> extRepo) CreateServiceWithExt(
        Mock<IActiveSessionRepository>? sessionRepo = null,
        Mock<ICafeRepository>? cafeRepo = null,
        Mock<IWalletService>? walletService = null)
    {
        sessionRepo ??= new Mock<IActiveSessionRepository>();
        cafeRepo ??= new Mock<ICafeRepository>();
        cafeRepo.Setup(r => r.GetActiveByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Name = "Test Cafe", Address = "123 Test St", IsActive = true });

        var posRepo = new Mock<ICafePosRepository>();
        var depositRepo = new Mock<IBookingDepositRepository>();
        var settlementService = new Mock<ISettlementService>();
        var reservationService = new Mock<IReservationService>();
        var lobbyRepo = new Mock<ILobbyRepository>();
        var reservationRepo = new Mock<IReservationRepository>();
        var walkInService = new Mock<IWalkInService>();
        var outboxRepo = new Mock<IOutboxRepository>();
        walletService ??= new Mock<IWalletService>();
        var posHubService = new Mock<IPosHubService>();
        var extRepo = new Mock<ISessionExtensionRequestRepository>();
        var pushService = new Mock<IPushNotificationService>();
        var dbOptions = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new BoardVerseDbContext(dbOptions);
        var logger = new Mock<ILogger<ActiveSessionService>>();

        var svc = new ActiveSessionService(
            cafeRepo.Object, sessionRepo.Object, posRepo.Object, depositRepo.Object,
            settlementService.Object, reservationService.Object, lobbyRepo.Object,
            reservationRepo.Object, walkInService.Object, outboxRepo.Object,
            walletService.Object, posHubService.Object, extRepo.Object,
            pushService.Object, db, logger.Object);

        return (svc, extRepo);
    }

    // ===== GAP-3: Timezone =====

    [Fact]
    public async Task GetCurrentSessionAsync_ReturnsVnTimezoneOffset()
    {
        var userId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var joinedAt = new DateTime(2026, 8, 22, 5, 0, 0, DateTimeKind.Utc); // 12:00 ICT
        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = userId,
            Status = GroupSessionStatus.Active,
            StartedAt = joinedAt,
            Cafe = new Cafe { Id = cafeId, Name = "VN Cafe", BasePrice = 60000m, BillingModel = CafePartnerBillingModel.TimeBased, IsActive = true, PartnerOperationalStatus = CafePartnerOperationalStatus.Active, Address = "123 St" },
            Games = new List<ActiveSessionGame> { new() { GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan" } } },
            Members = CreateMember(userId, IndividualSessionStatus.Playing, joinedAt)
        };

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByUserIdWithMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var (svc, extRepo) = CreateServiceWithExt(sessionRepo);
        extRepo.Setup(r => r.GetAllBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionExtensionRequest>());
        extRepo.Setup(r => r.GetPendingBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionExtensionRequest>());

        var result = await svc.GetCurrentSessionAsync(userId);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromHours(7), result!.JoinedAtOffset.Offset);
        // UTC 5:00 = Vietnam 12:00 (5 + 7 = 12)
        Assert.Equal(12, result.JoinedAtOffset.Hour);
        Assert.Equal(0, result.JoinedAtOffset.Minute);
    }

    // ===== Group total (host sees 16k for 8 members × 2k) =====

    private static List<ActiveSessionMember> CreateMembers(IEnumerable<Guid> userIds, IndividualSessionStatus status, DateTime joinedAt, int minutes, decimal subtotalEach)
    {
        return userIds.Select(id => new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            UserId = id,
            Status = status,
            JoinedAt = joinedAt,
            TotalMinutesPlayed = minutes,
            Subtotal = subtotalEach,
            PenaltyAmount = 0m,
            DepositAppliedAmount = 0m,
            TotalAmount = subtotalEach,
            User = CreateUser(id)
        }).ToList();
    }

    [Fact]
    public async Task GetCurrentSessionAsync_ReturnsGroupTotalAmount_ForUnpaidSession()
    {
        // Arrange: 8 members × 2000 = 16000 total, session already Unpaid after Checkout
        var hostId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var joinedAt = DateTime.UtcNow.AddMinutes(-30);

        var memberIds = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToList();
        memberIds.Insert(0, hostId);   // host is also a member for lookup
        var members = CreateMembers(memberIds, IndividualSessionStatus.Playing, joinedAt, 26, 2000m);

        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = hostId,
            Status = GroupSessionStatus.Unpaid,       // Already checked out → TotalAmount persisted
            StartedAt = joinedAt,
            EndedAt = joinedAt.AddMinutes(26),
            TotalAmount = 16000m,                    // 8 × 2000 from CompleteCheckoutAsync
            Subtotal = 16000m,
            PenaltyAmount = 0m,
            DepositAppliedAmount = 0m,
            Cafe = new Cafe
            {
                Id = cafeId, Name = "Boss cafe", BasePrice = 2000m,
                BillingModel = CafePartnerBillingModel.ByHour, TieredBlockMinutes = 15, TieredBlockRate = 1500m,
                IsActive = true, PartnerOperationalStatus = CafePartnerOperationalStatus.Active, Address = "123 St"
            },
            Games = new List<ActiveSessionGame> { new() { GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Werewolf" } } },
            Members = members
        };

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByUserIdWithMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var (svc, extRepo) = CreateServiceWithExt(sessionRepo);
        extRepo.Setup(r => r.GetAllBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionExtensionRequest>());
        extRepo.Setup(r => r.GetPendingBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionExtensionRequest>());

        var result = await svc.GetCurrentSessionAsync(hostId);

        Assert.NotNull(result);
        Assert.Equal(GroupSessionStatus.Unpaid, result!.SessionStatus);
        Assert.Equal(8, result.TotalGroupMembers);
        Assert.Equal(16000m, result.GroupTotalAmount);   // Host sees 16k total, not 2k personal
    }

    // ===== GAP-7/8: History pagination + date filter =====

    [Fact]
    public async Task GetSessionHistoryAsync_PassesCursorAndDateRange_ToRepository()
    {
        var userId = Guid.NewGuid();
        var sessionRepo = new Mock<IActiveSessionRepository>();
        DateTime? capturedBefore = null;
        DateTime? capturedFrom = null;
        DateTime? capturedTo = null;
        sessionRepo.Setup(r => r.GetHistoryByUserIdAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, int, DateTime?, DateTime?, DateTime?, CancellationToken>(
                (_, _, before, from, to, _) =>
                {
                    capturedBefore = before;
                    capturedFrom = from;
                    capturedTo = to;
                })
            .ReturnsAsync(new List<ActiveSession>());

        var (svc, _) = CreateServiceWithExt(sessionRepo);

        var before = new DateTime(2026, 8, 1);
        var from = new DateTime(2026, 7, 1);
        var to = new DateTime(2026, 7, 31);

        await svc.GetSessionHistoryAsync(userId, limit: 10, beforePaidAt: before, fromDate: from, toDate: to);

        Assert.Equal(before, capturedBefore);
        Assert.Equal(from, capturedFrom);
        Assert.Equal(to, capturedTo);
    }

    [Fact]
    public async Task GetSessionHistoryAsync_BackfillsVnTimezoneOffset_OnEachItem()
    {
        var userId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var joinedAt = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var paidAt = new DateTime(2026, 8, 1, 11, 0, 0, DateTimeKind.Utc);
        var session = CreatePaidSession(userId, cafeId, joinedAt, paidAt);

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetHistoryByUserIdAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession> { session });
        var cafeRepo = new Mock<ICafeRepository>();

        var (svc, _) = CreateServiceWithExt(sessionRepo, cafeRepo);

        var result = await svc.GetSessionHistoryAsync(userId);

        Assert.Single(result);
        var item = result[0];
        Assert.NotNull(item.PaidAtOffset);
        Assert.Equal(TimeSpan.FromHours(7), item.PaidAtOffset!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(7), item.JoinedAtOffset.Offset);
        Assert.Equal(GroupSessionStatus.Paid, item.SessionStatus);
    }

    // ===== GAP-13: LeftAt filter trong ExtendSession =====

    [Fact]
    public async Task ExtendSessionAsync_ThrowsNotFound_WhenMemberAlreadyLeft()
    {
        var userId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var joinedAt = DateTime.UtcNow.AddMinutes(-30);
        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = userId,
            Status = GroupSessionStatus.Active,
            StartedAt = joinedAt,
            Cafe = new Cafe { Id = cafeId, Name = "X", BillingModel = CafePartnerBillingModel.TimeBased, IsActive = true, PartnerOperationalStatus = CafePartnerOperationalStatus.Active, Address = "1" },
            Games = new List<ActiveSessionGame> { new() { GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan" } } },
            Members = CreateMember(userId, IndividualSessionStatus.Playing, joinedAt, DateTime.UtcNow.AddMinutes(-5), 25)
        };

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByUserIdWithMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var (svc, extRepo) = CreateServiceWithExt(sessionRepo);
        extRepo.Setup(r => r.GetAllBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionExtensionRequest>());

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => svc.ExtendSessionAsync(userId, 30));
        Assert.Contains("không tham gia", ex.Message);
    }

    // ===== GAP-12: Paused session cannot extend =====

    [Fact]
    public async Task ExtendSessionAsync_ThrowsConflict_WhenSessionPaused()
    {
        var userId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var joinedAt = DateTime.UtcNow.AddMinutes(-30);
        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = userId,
            Status = GroupSessionStatus.Active,
            IsPaused = true,
            PausedAt = DateTime.UtcNow,
            StartedAt = joinedAt,
            Cafe = new Cafe { Id = cafeId, Name = "X", BillingModel = CafePartnerBillingModel.TimeBased, IsActive = true, PartnerOperationalStatus = CafePartnerOperationalStatus.Active, Address = "1" },
            Games = new List<ActiveSessionGame> { new() { GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan" } } },
            Members = CreateMember(userId, IndividualSessionStatus.Playing, joinedAt)
        };

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByUserIdWithMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var (svc, extRepo) = CreateServiceWithExt(sessionRepo);
        extRepo.Setup(r => r.GetAllBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionExtensionRequest>());
        extRepo.Setup(r => r.GetPendingBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionExtensionRequest>());

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ExtendSessionAsync(userId, 30));
        Assert.Contains("tạm dừng", ex.Message);
    }

    // ===== GAP-1: null from repo → throw NotFound =====

    [Fact]
    public async Task GetCurrentSessionAsync_ThrowsNotFound_WhenUserHasOnlyPaidSession()
    {
        var userId = Guid.NewGuid();
        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByUserIdWithMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ActiveSession?)null);

        var (svc, _) = CreateServiceWithExt(sessionRepo);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetCurrentSessionAsync(userId));
    }

    // ===== GAP-9: LastExtensionRequest populate =====

    [Fact]
    public async Task GetCurrentSessionAsync_ReturnsLastExtensionRequest_WhenExists()
    {
        var userId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var joinedAt = DateTime.UtcNow.AddMinutes(-30);
        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = userId,
            Status = GroupSessionStatus.Active,
            StartedAt = joinedAt,
            Cafe = new Cafe { Id = cafeId, Name = "X", BasePrice = 60000m, BillingModel = CafePartnerBillingModel.TimeBased, IsActive = true, PartnerOperationalStatus = CafePartnerOperationalStatus.Active, Address = "1" },
            Games = new List<ActiveSessionGame> { new() { GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Catan" } } },
            Members = CreateMember(userId, IndividualSessionStatus.Playing, joinedAt)
        };

        var req = new SessionExtensionRequest
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            RequestedByUserId = userId,
            RequestedMinutes = 30,
            EstimatedAdditionalCostVnd = 30000m,
            Status = SessionExtensionRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };

        var sessionRepo = new Mock<IActiveSessionRepository>();
        sessionRepo.Setup(r => r.GetByUserIdWithMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var (svc, extRepo) = CreateServiceWithExt(sessionRepo);
        extRepo.Setup(r => r.GetAllBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionExtensionRequest> { req });
        extRepo.Setup(r => r.GetPendingBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionExtensionRequest> { req });

        var result = await svc.GetCurrentSessionAsync(userId);

        Assert.NotNull(result);
        Assert.NotNull(result!.LastExtensionRequest);
        Assert.Equal(30, result.LastExtensionRequest!.RequestedMinutes);
        Assert.Equal("Pending", result.LastExtensionRequest.Status);
    }
}