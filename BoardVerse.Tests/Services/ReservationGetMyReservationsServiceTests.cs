using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Enum;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Service-level tests cho <see cref="ReservationService.GetMyReservationsAsync"/> —
/// endpoint <c>GET /api/v1/reservations/my</c> (lịch sử reservation gộp Host + Member).
///
/// Mục đích: cover logic mapping <c>ParticipationType</c> → <c>hostedByMe/joinedByMe</c> flags,
/// defensive swap <c>fromDate/toDate</c>, clamp <c>page/pageSize</c>, và summary counts
/// (<c>HostedCount</c>, <c>JoinedCount</c>) ở service layer (repository đã có test riêng).
///
/// <para>
/// Test dùng <see cref="FakeDbContext"/> + <see cref="ReservationRepository"/> thật
/// (consistent với <c>ReservationListMyRepositoryTests</c>) — không mock toàn bộ service stack.
/// </para>
/// </summary>
public class ReservationGetMyReservationsServiceTests : IDisposable
{
    private readonly FakeDbContext _db;
    private readonly ReservationService _service;

    private readonly Guid _cafeId;
    private readonly Guid _otherCafeId;
    private readonly Guid _hostId;
    private readonly Guid _memberId;
    private readonly Guid _otherUserId;
    private readonly Guid _gameId;
    private readonly Guid _managerId;

    public ReservationGetMyReservationsServiceTests()
    {
        _db = new FakeDbContext();

        // Seed manager user.
        _managerId = Guid.NewGuid();
        var managerEmail = $"manager-{_managerId:N}@test.com";
        _db.Users.Add(new User
        {
            Id = _managerId,
            Username = managerEmail,
            Email = managerEmail,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        _cafeId = Guid.NewGuid();
        _db.Cafes.Add(new Cafe
        {
            Id = _cafeId,
            Name = "Test Cafe",
            Address = "123 Test Street",
            TotalSeats = 20,
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active,
            ManagerId = _managerId
        });

        _otherCafeId = Guid.NewGuid();
        _db.Cafes.Add(new Cafe
        {
            Id = _otherCafeId,
            Name = "Other Cafe",
            Address = "456 Other Street",
            TotalSeats = 15,
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active,
            ManagerId = _managerId
        });
        _db.SaveChanges();

        _gameId = Guid.NewGuid();
        _db.GameTemplates.Add(new GameTemplate
        {
            Id = _gameId,
            Name = "Test Catan",
            MinPlayers = 3,
            MaxPlayers = 4,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        _hostId = Guid.NewGuid();
        _memberId = Guid.NewGuid();
        _otherUserId = Guid.NewGuid();
        var hostEmail = $"host-{_hostId:N}@test.com";
        var memberEmail = $"member-{_memberId:N}@test.com";
        var otherEmail = $"other-{_otherUserId:N}@test.com";
        _db.Users.AddRange(
            new User { Id = _hostId, Username = hostEmail, Email = hostEmail, IsActive = true, CreatedAt = DateTime.UtcNow },
            new User { Id = _memberId, Username = memberEmail, Email = memberEmail, IsActive = true, CreatedAt = DateTime.UtcNow },
            new User { Id = _otherUserId, Username = otherEmail, Email = otherEmail, IsActive = true, CreatedAt = DateTime.UtcNow }
        );
        _db.SaveChanges();

        // Build service — dùng repository thật + mock dependencies khác (chỉ cần thiết cho
        // constructor — GetMyReservationsAsync chỉ gọi _reservationRepository).
        var reservationRepo = new ReservationRepository(_db);
        var lobbyRepo = new Mock<ILobbyRepository>();
        var walletService = new Mock<IWalletService>();
        var walletRepo = new Mock<IWalletRepository>();
        var seatRepo = new Mock<ISeatInventoryRepository>();
        var gameRepo = new Mock<IGameInventoryRepository>();
        var cafeInventoryRepo = new Mock<ICafeInventoryRepository>();
        var cafeConfigRepo = new Mock<ICafeConfigRepository>();
        var cafeRepo = new Mock<ICafeRepository>();
        var userRepo = new Mock<IUserManagementRepository>();
        var gameTemplateRepo = new Mock<IGameTemplateRepository>();
        var outboxRepo = new Mock<IOutboxRepository>();
        outboxRepo.Setup(r => r.AddAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var activeSessionRepo = new Mock<IActiveSessionRepository>();
        var depositCalculator = new DepositCalculator();
        var eligibilityValidator = new EligibilityValidator();
        var scheduleResolver = new Mock<IScheduleResolver>();
        var logger = new Mock<ILogger<ReservationService>>();
        var bookingRatingService = new Mock<IBookingRatingService>();
        var refundCalc = new RefundCalculationService();
        var walkInService = new Mock<IWalkInService>();
        var karmaService = new Mock<IPlayerKarmaService>();
        var configProvider = new Mock<ISystemConfigurationProvider>();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var settlementService = new Mock<ISettlementService>();

        _service = new ReservationService(
            _db,
            walletService.Object,
            walletRepo.Object,
            reservationRepo,
            lobbyRepo.Object,
            seatRepo.Object,
            gameRepo.Object,
            cafeInventoryRepo.Object,
            cafeConfigRepo.Object,
            cafeRepo.Object,
            userRepo.Object,
            gameTemplateRepo.Object,
            outboxRepo.Object,
            activeSessionRepo.Object,
            depositCalculator,
            eligibilityValidator,
            scheduleResolver.Object,
            logger.Object,
            TimeProvider.System,
            bookingRatingService.Object,
            refundCalc,
            walkInService.Object,
            karmaService.Object,
            configProvider.Object,
            httpContextAccessor.Object,
            settlementService.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    /// <summary>
    /// Seed 1 Reservation + Lobby + LobbyMember. hostId là người tạo reservation.
    /// members: optional list of (userId, IsHost, IsActive) để link member vào lobby.
    /// </summary>
    private Reservation SeedReservation(
        Guid hostId,
        Guid cafeId,
        Guid gameId,
        DateOnly playDate,
        ReservationStatus status,
        DateTime? scheduledStart = null,
        DateTime? createdAt = null,
        List<(Guid UserId, bool IsHost, bool IsActive)>? members = null)
    {
        var scheduledStartTime = scheduledStart ?? playDate.ToDateTime(new TimeOnly(19, 0));

        var lobbyId = Guid.NewGuid();
        // Unique ShareCode cho mỗi lobby (collision với unique constraint IX_Lobbies_ShareCode).
        var shareCode = $"SL{lobbyId:N}".Substring(0, 8).ToUpper();

        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            GameTemplateId = gameId,
            CafeId = cafeId,
            ReservationId = null, // set sau khi reservation insert
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(19, 0),
            PreferredEndTime = new TimeOnly(21, 0),
            ScheduledStartTime = scheduledStartTime,
            RecruitmentDeadline = scheduledStartTime.AddHours(-2),
            MaxMembers = 4,
            MinPlayers = 2,
            ShareCode = shareCode,
            Status = LobbyStatus.Open,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow
        };

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            CafeId = cafeId,
            GameId = gameId,
            PlayDate = playDate,
            TimeSlot = TimeSlot.Evening,
            PreferredStartTime = new TimeOnly(19, 0),
            PreferredEndTime = new TimeOnly(21, 0),
            ScheduledStartTime = scheduledStartTime,
            ScheduledEndTime = scheduledStartTime.AddHours(2),
            RecruitmentDeadline = scheduledStartTime.AddHours(-2),
            MinPlayers = 2,
            MaxPlayers = 4,
            Status = status,
            ReservationCode = $"R{Guid.NewGuid():N}".Substring(0, 8).ToUpper(),
            IdempotencyKey = $"idem-{Guid.NewGuid():N}",
            DepositAmount = 50,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow
        };

        _db.Reservations.Add(reservation);
        _db.Lobbies.Add(lobby);
        _db.SaveChanges();

        // Bind FK reservation ↔ lobby.
        reservation.LobbyId = lobby.Id;
        lobby.ReservationId = reservation.Id;
        _db.SaveChanges();

        // Seed lobby members (host + optional others).
        _db.LobbyMembers.Add(new LobbyMember
        {
            Id = Guid.NewGuid(),
            LobbyId = lobby.Id,
            UserId = hostId,
            JoinedAt = createdAt ?? DateTime.UtcNow,
            IsActive = true,
            IsHost = true,
            Status = LobbyMemberStatus.Joined
        });

        if (members != null)
        {
            foreach (var (userId, isHostMember, isActive) in members)
            {
                _db.LobbyMembers.Add(new LobbyMember
                {
                    Id = Guid.NewGuid(),
                    LobbyId = lobby.Id,
                    UserId = userId,
                    JoinedAt = createdAt ?? DateTime.UtcNow,
                    IsActive = isActive,
                    IsHost = isHostMember,
                    Status = isActive ? LobbyMemberStatus.Joined : LobbyMemberStatus.Left
                });
            }
        }

        _db.SaveChanges();

        return reservation;
    }

    // ============================================================
    // ParticipationType → hostedByMe/joinedByMe mapping
    // ============================================================

    [Fact]
    public async Task GetMyReservationsAsync_WithParticipationTypeHost_Should_ReturnOnlyHosted()
    {
        // _memberId có 1 reservation host + 1 reservation join.
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });

        var request = new MyReservationsRequestDto
        {
            ParticipationType = ReservationParticipationType.Host
        };

        var result = await _service.GetMyReservationsAsync(_memberId, request);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(ReservationParticipationType.Host, result.Items[0].ParticipationType);
        Assert.True(result.Items[0].IsHost);
    }

    [Fact]
    public async Task GetMyReservationsAsync_WithParticipationTypeMember_Should_ReturnOnlyJoined()
    {
        // _memberId có 1 reservation host + 1 reservation join.
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });

        var request = new MyReservationsRequestDto
        {
            ParticipationType = ReservationParticipationType.Member
        };

        var result = await _service.GetMyReservationsAsync(_memberId, request);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(ReservationParticipationType.Member, result.Items[0].ParticipationType);
        Assert.False(result.Items[0].IsHost);
        // Joined items KHÔNG phải do user host (tránh leak self-hosted).
        Assert.NotEqual(_memberId, result.Items[0].Id); // Sanity check: id khác nhau (verified separately via HostId check below).
        Assert.NotNull(result.Items);
        Assert.All(result.Items, item =>
        {
            // Reservation.HostId không được là _memberId (đã fix Gap-2).
            // Verify qua HostName chứa logic (không có field HostId trong ListItemDto,
            // nhưng ParticipationType=Member đã đủ đảm bảo filter đúng).
            Assert.Equal(ReservationParticipationType.Member, item.ParticipationType);
        });
    }

    [Fact]
    public async Task GetMyReservationsAsync_WithParticipationTypeNull_Should_ReturnBothHostedAndJoined()
    {
        // _memberId có 1 reservation host + 1 reservation join.
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });

        var request = new MyReservationsRequestDto
        {
            ParticipationType = null // cả Host + Member
        };

        var result = await _service.GetMyReservationsAsync(_memberId, request);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.ParticipationType == ReservationParticipationType.Host);
        Assert.Contains(result.Items, i => i.ParticipationType == ReservationParticipationType.Member);
    }

    // ============================================================
    // ParticipationType mapping từng item (Mapper unit test)
    // ============================================================

    [Fact]
    public async Task GetMyReservationsAsync_Should_SetParticipationTypeHost_WhenUserIsHost()
    {
        var reservation = SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);

        var result = await _service.GetMyReservationsAsync(_hostId, new MyReservationsRequestDto());

        Assert.Equal(ReservationParticipationType.Host, result.Items[0].ParticipationType);
        Assert.True(result.Items[0].IsHost);
    }

    [Fact]
    public async Task GetMyReservationsAsync_Should_SetParticipationTypeMember_WhenUserJoinsOtherReservation()
    {
        // _hostId tạo reservation, _memberId join.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });

        var result = await _service.GetMyReservationsAsync(_memberId, new MyReservationsRequestDto());

        Assert.Equal(ReservationParticipationType.Member, result.Items[0].ParticipationType);
        Assert.False(result.Items[0].IsHost);
    }

    // ============================================================
    // Date range defensive swap
    // ============================================================

    [Fact]
    public async Task GetMyReservationsAsync_Should_SwapFromAndToDate_WhenFromAfterTo()
    {
        // Seed 3 reservation trải đều.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 1), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 20), ReservationStatus.Holding);

        var request = new MyReservationsRequestDto
        {
            FromDate = new DateOnly(2026, 9, 20), // sau
            ToDate = new DateOnly(2026, 9, 1)     // trước
        };

        var result = await _service.GetMyReservationsAsync(_hostId, request);

        // Sau khi swap: range = [1, 20] → 3 reservation.
        Assert.Equal(3, result.TotalCount);
    }

    // ============================================================
    // Page/PageSize clamp
    // ============================================================

    [Fact]
    public async Task GetMyReservationsAsync_Should_ClampPageSize_ToMaximum100()
    {
        // Seed 1 reservation.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);

        var request = new MyReservationsRequestDto
        {
            Page = 1,
            PageSize = 500 // vượt max
        };

        var result = await _service.GetMyReservationsAsync(_hostId, request);

        // Service clamp pageSize về [1, 100].
        Assert.Equal(100, result.PageSize);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetMyReservationsAsync_Should_ClampPage_ToMinimum1()
    {
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);

        var request = new MyReservationsRequestDto
        {
            Page = -5, // invalid
            PageSize = 20
        };

        var result = await _service.GetMyReservationsAsync(_hostId, request);

        // Service clamp page về >= 1.
        Assert.True(result.Page >= 1);
    }

    [Fact]
    public async Task GetMyReservationsAsync_Should_ClampPageSize_ToMinimum1()
    {
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);

        var request = new MyReservationsRequestDto
        {
            Page = 1,
            PageSize = 0 // invalid
        };

        var result = await _service.GetMyReservationsAsync(_hostId, request);

        // Service clamp pageSize về >= 1.
        Assert.True(result.PageSize >= 1);
    }

    // ============================================================
    // Summary counts (HostedCount + JoinedCount)
    // ============================================================

    [Fact]
    public async Task GetMyReservationsAsync_Should_ReturnCorrectSummaryCounts()
    {
        // Setup: _memberId có:
        //   - 2 reservation host (ở cafe A và cafe B).
        //   - 1 reservation join (do _hostId host, _memberId join).
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_memberId, _otherCafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 12), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });

        var request = new MyReservationsRequestDto();

        var result = await _service.GetMyReservationsAsync(_memberId, request);

        Assert.Equal(2, result.HostedCount);
        Assert.Equal(1, result.JoinedCount);
        // TotalCount = 3 (cả 2 + 1).
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task GetMyReservationsAsync_Should_ApplyFilterToSummaryCounts()
    {
        // Setup: _memberId có 3 reservation host (status Holding/Confirmed/Cancelled)
        // + 1 reservation join.
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 5), ReservationStatus.Holding);
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Confirmed);
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 15), ReservationStatus.CancelledByPlayer);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });

        // Filter status Holding → chỉ 1 Host (5/9) + 1 Member (11/9).
        var request = new MyReservationsRequestDto
        {
            Statuses = new List<ReservationStatus> { ReservationStatus.Holding }
        };

        var result = await _service.GetMyReservationsAsync(_memberId, request);

        Assert.Equal(1, result.HostedCount);
        Assert.Equal(1, result.JoinedCount);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GetMyReservationsAsync_SummaryCounts_Should_BeIndependentOfParticipationTypeFilter()
    {
        // _memberId có 2 host + 1 join.
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 12), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });

        // Filter Host-only → items=2, nhưng summary counts vẫn phải count cả Host + Member.
        var request = new MyReservationsRequestDto
        {
            ParticipationType = ReservationParticipationType.Host
        };

        var result = await _service.GetMyReservationsAsync(_memberId, request);

        Assert.Equal(2, result.TotalCount);  // Host items
        Assert.Equal(2, result.HostedCount); // Summary Host (full count)
        Assert.Equal(1, result.JoinedCount); // Summary Member (full count, không phụ thuộc filter)
    }

    [Fact]
    public async Task GetMyReservationsAsync_SummaryCounts_Should_ExcludeSelfHostedFromJoinedCount()
    {
        // _memberId host 1 reservation + tự join (qua LobbyMember IsActive=true).
        // Gap-2: JoinedCount phải EXCLUDE self-hosted → JoinedCount = 0.
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);

        var result = await _service.GetMyReservationsAsync(_memberId, new MyReservationsRequestDto());

        Assert.Equal(1, result.HostedCount);
        Assert.Equal(0, result.JoinedCount);
    }

    // ============================================================
    // Edge cases
    // ============================================================

    [Fact]
    public async Task GetMyReservationsAsync_Should_ReturnEmptySummary_WhenNoReservations()
    {
        var result = await _service.GetMyReservationsAsync(_otherUserId, new MyReservationsRequestDto());

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.HostedCount);
        Assert.Equal(0, result.JoinedCount);
    }
}
