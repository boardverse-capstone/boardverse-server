using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data.Repositories;
using BoardVerse.Tests.Helpers;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="ReservationRepository.GetListAsync"/> — endpoint
/// <c>GET /api/v1/reservations/my</c> (lịch sử reservation gộp Host + Member).
///
/// Coverage:
/// - fromDate / toDate range filter (push xuống SQL).
/// - Defensive swap khi caller truyền fromDate &gt; toDate.
/// - Sort order: <c>PlayDate desc → ScheduledStartTime desc → CreatedAt desc</c>.
/// - hostedByMe / joinedByMe filter (default = cả 2).
/// - Status filter.
/// - Cafe filter.
/// - Pagination (page + pageSize).
/// </summary>
public class ReservationListMyRepositoryTests : IDisposable
{
    private readonly FakeDbContext _db;
    private readonly ReservationRepository _repo;

    private readonly Guid _cafeId;
    private readonly Guid _otherCafeId;
    private readonly Guid _hostId;
    private readonly Guid _memberId;
    private readonly Guid _otherUserId;
    private readonly Guid _gameId;
    private readonly Guid _otherGameId;
    private readonly Guid _managerId;

    public ReservationListMyRepositoryTests()
    {
        _db = new FakeDbContext();
        _repo = new ReservationRepository(_db);

        // Seed manager user (FK từ Cafe.ManagerId).
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

        _otherGameId = Guid.NewGuid();
        _db.GameTemplates.Add(new GameTemplate
        {
            Id = _otherGameId,
            Name = "Test Splendor",
            MinPlayers = 2,
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
    // Range filter (fromDate / toDate)
    // ============================================================

    [Fact]
    public async Task GetListAsync_Should_FilterByFromDateInclusive()
    {
        // 3 reservation với playDate: 01/09, 10/09, 20/09.
        // Filter fromDate=10/09 → chỉ trả 10/09 và 20/09 (inclusive).
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 1), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 20), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: new DateOnly(2026, 9, 10),
            toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(2, totalCount);
        Assert.Equal(2, items.Count);
        Assert.All(items, r => Assert.True(r.PlayDate >= new DateOnly(2026, 9, 10)));
    }

    [Fact]
    public async Task GetListAsync_Should_FilterByToDateInclusive()
    {
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 1), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 20), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null,
            toDate: new DateOnly(2026, 9, 10),
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(2, totalCount);
        Assert.All(items, r => Assert.True(r.PlayDate <= new DateOnly(2026, 9, 10)));
    }

    [Fact]
    public async Task GetListAsync_Should_FilterByFromAndToDateRange()
    {
        // Range 5/09 → 15/09 (inclusive cả 2 đầu).
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 1), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 5), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 15), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 20), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: new DateOnly(2026, 9, 5),
            toDate: new DateOnly(2026, 9, 15),
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(3, totalCount);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task GetListAsync_Should_SwapFromAndToDate_WhenFromAfterTo()
    {
        // Caller lỡ truyền fromDate > toDate → repository tự swap để trả kết quả đúng.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 1), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 20), ReservationStatus.Holding);

        // Truyền SAI thứ tự: fromDate=20, toDate=1.
        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: new DateOnly(2026, 9, 20),
            toDate: new DateOnly(2026, 9, 1),
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        // Phải trả về 3 (range sau khi swap: 1 → 20).
        Assert.Equal(3, totalCount);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task GetListAsync_Should_HandleBothFromAndToDateNull()
    {
        // Không truyền range → trả tất cả.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 1), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(2, totalCount);
    }

    // ============================================================
    // Sort order
    // ============================================================

    [Fact]
    public async Task GetListAsync_Should_SortByPlayDateDescending()
    {
        // Seed theo thứ tự không đúng sort để verify.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 5), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 20), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);

        var (items, _) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(3, items.Count);
        Assert.Equal(new DateOnly(2026, 9, 20), items[0].PlayDate);
        Assert.Equal(new DateOnly(2026, 9, 10), items[1].PlayDate);
        Assert.Equal(new DateOnly(2026, 9, 5), items[2].PlayDate);
    }

    [Fact]
    public async Task GetListAsync_Should_SortByScheduledStartTimeDescending_WhenSamePlayDate()
    {
        // Cùng playDate → tie-break bằng ScheduledStartTime desc.
        var playDate = new DateOnly(2026, 9, 10);

        SeedReservation(_hostId, _cafeId, _gameId, playDate, ReservationStatus.Holding,
            scheduledStart: playDate.ToDateTime(new TimeOnly(10, 0)));
        SeedReservation(_hostId, _cafeId, _gameId, playDate, ReservationStatus.Holding,
            scheduledStart: playDate.ToDateTime(new TimeOnly(20, 0)));
        SeedReservation(_hostId, _cafeId, _gameId, playDate, ReservationStatus.Holding,
            scheduledStart: playDate.ToDateTime(new TimeOnly(15, 0)));

        var (items, _) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(3, items.Count);
        Assert.Equal(new TimeOnly(20, 0), TimeOnly.FromDateTime(items[0].ScheduledStartTime));
        Assert.Equal(new TimeOnly(15, 0), TimeOnly.FromDateTime(items[1].ScheduledStartTime));
        Assert.Equal(new TimeOnly(10, 0), TimeOnly.FromDateTime(items[2].ScheduledStartTime));
    }

    // ============================================================
    // hostedByMe / joinedByMe
    // ============================================================

    [Fact]
    public async Task GetListAsync_Should_ReturnOnlyHosted_WhenHostedByMeTrue_JoinedByMeFalse()
    {
        // _hostId host 2 reservation (có _memberId join 1 cái).
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding);

        // _memberId host 1 reservation riêng (không liên quan).
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 12), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _memberId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        // Chỉ trả 1: reservation do _memberId host.
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal(_memberId, items[0].HostId);
    }

    [Fact]
    public async Task GetListAsync_Should_ReturnOnlyJoined_WhenHostedByMeFalse_JoinedByMeTrue()
    {
        // _hostId host 2 reservation (có _memberId join 1 cái).
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding);

        // _memberId host 1 reservation riêng (để verify filter chỉ trả joined, không trả hosted).
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 12), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _memberId, hostedByMe: false, joinedByMe: true,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        // Gap-2 fix (2026-09-02): Member-only filter EXCLUDE self-hosted reservation.
        // Trước đây expect 2 vì user tự host cũng có LobbyMember IsActive=true.
        // Sau fix: chỉ trả reservation do _hostId host (mà _memberId join vào).
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.All(items, r => Assert.NotEqual(_memberId, r.HostId));
        Assert.Equal(_hostId, items[0].HostId);
    }

    [Fact]
    public async Task GetListAsync_Should_ReturnBothHostedAndJoined_WhenBothTrue()
    {
        // _memberId host 1 reservation + join 1 reservation do _hostId tạo.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _memberId, hostedByMe: true, joinedByMe: true,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(2, totalCount);
    }

    [Fact]
    public async Task GetListAsync_Should_NotReturnOtherUsersReservation()
    {
        // _otherUserId host 1 reservation — _memberId KHÔNG liên quan.
        SeedReservation(_otherUserId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, true) });

        var (items, totalCount) = await _repo.GetListAsync(
            _memberId, hostedByMe: true, joinedByMe: true,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        // Chỉ trả 1: cái _memberId join.
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.DoesNotContain(items, r => r.HostId == _otherUserId);
    }

    [Fact]
    public async Task GetListAsync_Should_ExcludeInactiveMemberFromJoinedFilter()
    {
        // _memberId join nhưng IsActive = false (đã rời lobby) → KHÔNG trả.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding,
            members: new List<(Guid, bool, bool)> { (_memberId, false, false) });

        var (items, totalCount) = await _repo.GetListAsync(
            _memberId, hostedByMe: false, joinedByMe: true,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(0, totalCount);
        Assert.Empty(items);
    }

    // ============================================================
    // Status filter
    // ============================================================

    [Fact]
    public async Task GetListAsync_Should_FilterByStatusList()
    {
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Confirmed);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 12), ReservationStatus.CancelledByPlayer);

        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: new List<ReservationStatus> { ReservationStatus.Holding, ReservationStatus.Confirmed },
            playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(2, totalCount);
        Assert.All(items, r => Assert.Contains(r.Status,
            new[] { ReservationStatus.Holding, ReservationStatus.Confirmed }));
    }

    [Fact]
    public async Task GetListAsync_Should_ReturnAllStatuses_WhenStatusListIsNull()
    {
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Confirmed);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 12), ReservationStatus.Completed);

        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null,
            playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(3, totalCount);
    }

    // ============================================================
    // Cafe filter
    // ============================================================

    [Fact]
    public async Task GetListAsync_Should_FilterByCafeId()
    {
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);
        SeedReservation(_hostId, _otherCafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null,
            playDate: null,
            fromDate: null, toDate: null,
            cafeId: _cafeId,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal(_cafeId, items[0].CafeId);
    }

    // ============================================================
    // Pagination
    // ============================================================

    [Fact]
    public async Task GetListAsync_Should_PaginateResults()
    {
        // Seed 5 reservation.
        for (int i = 1; i <= 5; i++)
        {
            SeedReservation(_hostId, _cafeId, _gameId,
                new DateOnly(2026, 9, i), ReservationStatus.Holding);
        }

        // Page 1, pageSize=2 → 2 items, totalCount=5.
        var (page1Items, totalCount1) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 2,
            cancellationToken: default);

        Assert.Equal(5, totalCount1);
        Assert.Equal(2, page1Items.Count);

        // Page 2, pageSize=2 → 2 items khác.
        var (page2Items, totalCount2) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 2, pageSize: 2,
            cancellationToken: default);

        Assert.Equal(5, totalCount2);
        Assert.Equal(2, page2Items.Count);

        // Page 3, pageSize=2 → 1 item cuối.
        var (page3Items, _) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 3, pageSize: 2,
            cancellationToken: default);

        Assert.Single(page3Items);

        // Verify no overlap giữa page 1 và page 2.
        var page1Ids = page1Items.Select(r => r.Id).ToHashSet();
        var page2Ids = page2Items.Select(r => r.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
    }

    // ============================================================
    // Combined filter (range + status + cafe + participation)
    // ============================================================

    [Fact]
    public async Task GetListAsync_Should_CombineAllFiltersCorrectly()
    {
        // _hostId host 3 reservation ở cafe A: Holding/Confirmed/Cancelled.
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 5), ReservationStatus.Holding);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Confirmed);
        SeedReservation(_hostId, _cafeId, _gameId, new DateOnly(2026, 9, 15), ReservationStatus.CancelledByPlayer);

        // _hostId host 1 reservation ở cafe B.
        SeedReservation(_hostId, _otherCafeId, _gameId, new DateOnly(2026, 9, 12), ReservationStatus.Confirmed);

        // _memberId host 1 reservation ở cafe A (không liên quan).
        SeedReservation(_memberId, _cafeId, _gameId, new DateOnly(2026, 9, 11), ReservationStatus.Confirmed);

        // Filter: range 5-15, cafe A, status Holding/Confirmed, hostedByMe.
        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: new List<ReservationStatus> { ReservationStatus.Holding, ReservationStatus.Confirmed },
            playDate: null,
            fromDate: new DateOnly(2026, 9, 5),
            toDate: new DateOnly(2026, 9, 15),
            cafeId: _cafeId,
            page: 1, pageSize: 20,
            cancellationToken: default);

        // Expected: 2 (5/9 Holding + 10/9 Confirmed, đều ở cafe A, đều active status).
        Assert.Equal(2, totalCount);
        Assert.Equal(2, items.Count);
        Assert.All(items, r => Assert.Equal(_cafeId, r.CafeId));
        Assert.All(items, r => Assert.Equal(_hostId, r.HostId));
    }

    // ============================================================
    // Edge cases
    // ============================================================

    [Fact]
    public async Task GetListAsync_Should_ReturnEmpty_WhenNoReservationMatches()
    {
        SeedReservation(_otherUserId, _cafeId, _gameId, new DateOnly(2026, 9, 10), ReservationStatus.Holding);

        var (items, totalCount) = await _repo.GetListAsync(
            _hostId, hostedByMe: true, joinedByMe: false,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(0, totalCount);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetListAsync_Should_NotIncludeReservationWithNullLobby_WhenFilteringJoined()
    {
        // Reservation walk-in (không qua lobby) — LobbyId = null.
        // _memberId join không thể qua reservation này (vì không có lobby).
        var walkInReservation = new Reservation
        {
            Id = Guid.NewGuid(),
            HostId = _hostId,
            CafeId = _cafeId,
            GameId = _gameId,
            PlayDate = new DateOnly(2026, 9, 10),
            TimeSlot = TimeSlot.Evening,
            PreferredStartTime = new TimeOnly(19, 0),
            PreferredEndTime = new TimeOnly(21, 0),
            ScheduledStartTime = new DateTime(2026, 9, 10, 19, 0, 0),
            ScheduledEndTime = new DateTime(2026, 9, 10, 21, 0, 0),
            RecruitmentDeadline = new DateTime(2026, 9, 10, 17, 0, 0),
            MinPlayers = 2,
            MaxPlayers = 4,
            Status = ReservationStatus.Holding,
            ReservationCode = $"WK{Guid.NewGuid():N}".Substring(0, 8).ToUpper(),
            IdempotencyKey = $"idem-{Guid.NewGuid():N}",
            DepositAmount = 50,
            LobbyId = null, // walk-in
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Reservations.Add(walkInReservation);
        _db.SaveChanges();

        var (items, totalCount) = await _repo.GetListAsync(
            _memberId, hostedByMe: false, joinedByMe: true,
            statuses: null, playDate: null,
            fromDate: null, toDate: null,
            cafeId: null,
            page: 1, pageSize: 20,
            cancellationToken: default);

        Assert.Equal(0, totalCount);
        Assert.Empty(items);
    }
}
