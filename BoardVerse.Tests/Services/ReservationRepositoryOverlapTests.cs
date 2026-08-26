using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data.Repositories;
using BoardVerse.Tests.Helpers;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho ReservationRepository.GetOverlappingReservationsAsync — Gap G2.
/// BR §21A.2: Reservation overlap với [startTime, endTime] khi
///   reservation.ScheduledStartTime &lt; endTime
///   &amp;&amp; reservation.ScheduledEndTime &gt; startTime
/// </summary>
public class ReservationRepositoryOverlapTests : IDisposable
{
    private readonly FakeDbContext _db;
    private readonly ReservationRepository _repo;
    private readonly Guid _cafeId = Guid.NewGuid();

    public ReservationRepositoryOverlapTests()
    {
        _db = new FakeDbContext();
        _repo = new ReservationRepository(_db);

        // Seed manager user trước (FK từ Cafe.ManagerId).
        // Email phải unique trên DB dùng chung (FakeDbContext dùng Postgres thật
        // khi có DATABASE_URL) → thêm GUID để tránh conflict.
        var managerId = Guid.NewGuid();
        _managerId = managerId;
        var managerEmail = $"manager-{managerId:N}@test.com";
        _db.Users.Add(new User
        {
            Id = managerId,
            Username = managerEmail,
            Email = managerEmail,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        // Seed cafe
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
        _db.SaveChanges();

        // Seed game template (FK từ Reservation.GameId)
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

        // Seed host user (FK từ Reservation.HostId)
        _hostId = Guid.NewGuid();
        var hostEmail = $"host-{_hostId:N}@test.com";
        _db.Users.Add(new User
        {
            Id = _hostId,
            Username = hostEmail,
            Email = hostEmail,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    private readonly Guid _gameId;
    private readonly Guid _hostId;
    private readonly Guid _managerId;

    public void Dispose()
    {
        _db.Dispose();
    }

    private Reservation SeedReservation(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        ReservationStatus status,
        int maxPlayers = 4)
    {
        var uniqueId = Guid.NewGuid();
        var reservation = new Reservation
        {
            Id = uniqueId,
            HostId = _hostId,
            CafeId = _cafeId,
            GameId = _gameId,
            PlayDate = DateOnly.FromDateTime(scheduledStart),
            TimeSlot = TimeSlot.Afternoon,
            ScheduledStartTime = scheduledStart,
            ScheduledEndTime = scheduledEnd,
            RecruitmentDeadline = scheduledStart.AddHours(-2),
            MinPlayers = 2,
            MaxPlayers = maxPlayers,
            Status = status,
            ReservationCode = $"R{uniqueId.ToString("N")[..7].ToUpper()}",
            IdempotencyKey = $"idem-{uniqueId:N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Reservations.Add(reservation);
        _db.SaveChanges();
        return reservation;
    }

    [Fact]
    public async Task GetOverlappingReservationsAsync_Should_ReturnReservationsInRange()
    {
        var queryStart = new DateTime(2026, 8, 20, 13, 0, 0);
        var queryEnd = new DateTime(2026, 8, 20, 15, 0, 0);

        SeedReservation(
            new DateTime(2026, 8, 20, 13, 0, 0),
            new DateTime(2026, 8, 20, 15, 0, 0),
            ReservationStatus.Holding);

        var result = await _repo.GetOverlappingReservationsAsync(_cafeId, queryStart, queryEnd);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetOverlappingReservationsAsync_Should_IncludeAllStatuses()
    {
        // Query phải trả về MỌI status (caller filter theo status trong service).
        // Repository chỉ lo overlap thời gian.
        var queryStart = new DateTime(2026, 8, 20, 13, 0, 0);
        var queryEnd = new DateTime(2026, 8, 20, 15, 0, 0);

        SeedReservation(
            new DateTime(2026, 8, 20, 13, 0, 0),
            new DateTime(2026, 8, 20, 15, 0, 0),
            ReservationStatus.Holding);
        SeedReservation(
            new DateTime(2026, 8, 20, 14, 0, 0),
            new DateTime(2026, 8, 20, 16, 0, 0),
            ReservationStatus.CancelledByPlayer); // overlap về time, status terminal
        SeedReservation(
            new DateTime(2026, 8, 20, 12, 0, 0),
            new DateTime(2026, 8, 20, 14, 0, 0),
            ReservationStatus.Confirmed);

        var result = await _repo.GetOverlappingReservationsAsync(_cafeId, queryStart, queryEnd);

        // Cả 3 reservation đều overlap → repository trả hết, service sẽ filter.
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetOverlappingReservationsAsync_Should_ExcludeNonOverlappingReservation()
    {
        // Reservation KẾT THÚC trước query start → KHÔNG overlap.
        var queryStart = new DateTime(2026, 8, 20, 13, 0, 0);
        var queryEnd = new DateTime(2026, 8, 20, 15, 0, 0);

        SeedReservation(
            new DateTime(2026, 8, 20, 10, 0, 0),
            new DateTime(2026, 8, 20, 12, 0, 0), // ends BEFORE queryStart
            ReservationStatus.Holding);
        SeedReservation(
            new DateTime(2026, 8, 20, 16, 0, 0), // starts AFTER queryEnd
            new DateTime(2026, 8, 20, 18, 0, 0),
            ReservationStatus.Holding);
        SeedReservation(
            new DateTime(2026, 8, 20, 13, 0, 0),
            new DateTime(2026, 8, 20, 15, 0, 0),
            ReservationStatus.Holding);

        var result = await _repo.GetOverlappingReservationsAsync(_cafeId, queryStart, queryEnd);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetOverlappingReservationsAsync_Should_FilterByCafe()
    {
        var queryStart = new DateTime(2026, 8, 20, 13, 0, 0);
        var queryEnd = new DateTime(2026, 8, 20, 15, 0, 0);

        SeedReservation(
            new DateTime(2026, 8, 20, 13, 0, 0),
            new DateTime(2026, 8, 20, 15, 0, 0),
            ReservationStatus.Holding);

        // Insert 1 reservation ở cafe khác
        var otherCafeId = Guid.NewGuid();
        _db.Cafes.Add(new Cafe
        {
            Id = otherCafeId,
            Name = "Other Cafe",
            Address = "456 Other Street",
            TotalSeats = 10,
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active,
            ManagerId = _managerId
        });
        _db.SaveChanges();
        var otherReservationId = Guid.NewGuid();
        var otherReservation = new Reservation
        {
            Id = otherReservationId,
            HostId = _hostId,
            CafeId = otherCafeId,
            GameId = _gameId,
            PlayDate = DateOnly.FromDateTime(queryStart),
            TimeSlot = TimeSlot.Afternoon,
            ScheduledStartTime = queryStart,
            ScheduledEndTime = queryEnd,
            RecruitmentDeadline = queryStart.AddHours(-2),
            MinPlayers = 2,
            MaxPlayers = 4,
            Status = ReservationStatus.Holding,
            ReservationCode = $"O{otherReservationId.ToString("N")[..7].ToUpper()}",
            IdempotencyKey = $"idem-{otherReservationId:N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Reservations.Add(otherReservation);
        _db.SaveChanges();

        var result = await _repo.GetOverlappingReservationsAsync(_cafeId, queryStart, queryEnd);

        Assert.Single(result);
        Assert.Equal(_cafeId, result[0].CafeId);
    }

    [Fact]
    public async Task GetOverlappingReservationsAsync_Should_HandleEdgeCase_StartAtQueryEnd()
    {
        // Edge case: ScheduledStartTime == queryEnd → KHÔNG overlap (strict <).
        var queryStart = new DateTime(2026, 8, 20, 13, 0, 0);
        var queryEnd = new DateTime(2026, 8, 20, 15, 0, 0);

        SeedReservation(
            new DateTime(2026, 8, 20, 15, 0, 0), // starts == queryEnd
            new DateTime(2026, 8, 20, 17, 0, 0),
            ReservationStatus.Holding);

        var result = await _repo.GetOverlappingReservationsAsync(_cafeId, queryStart, queryEnd);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOverlappingReservationsAsync_Should_HandleEdgeCase_EndAtQueryStart()
    {
        // Edge case: ScheduledEndTime == queryStart → KHÔNG overlap (strict >).
        var queryStart = new DateTime(2026, 8, 20, 13, 0, 0);
        var queryEnd = new DateTime(2026, 8, 20, 15, 0, 0);

        SeedReservation(
            new DateTime(2026, 8, 20, 11, 0, 0),
            new DateTime(2026, 8, 20, 13, 0, 0), // ends == queryStart
            ReservationStatus.Holding);

        var result = await _repo.GetOverlappingReservationsAsync(_cafeId, queryStart, queryEnd);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOverlappingReservationsAsync_Should_DetectPartialOverlap()
    {
        // Reservation bắt đầu trước, kết thúc giữa query → overlap.
        var queryStart = new DateTime(2026, 8, 20, 13, 0, 0);
        var queryEnd = new DateTime(2026, 8, 20, 15, 0, 0);

        SeedReservation(
            new DateTime(2026, 8, 20, 12, 0, 0),
            new DateTime(2026, 8, 20, 14, 0, 0),
            ReservationStatus.Holding);
        // Reservation bắt đầu giữa, kết thúc sau query → overlap.
        SeedReservation(
            new DateTime(2026, 8, 20, 14, 0, 0),
            new DateTime(2026, 8, 20, 16, 0, 0),
            ReservationStatus.Holding);

        var result = await _repo.GetOverlappingReservationsAsync(_cafeId, queryStart, queryEnd);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetOverlappingReservationsAsync_Should_ReturnEmpty_WhenNoReservations()
    {
        var result = await _repo.GetOverlappingReservationsAsync(
            _cafeId, DateTime.UtcNow, DateTime.UtcNow.AddHours(2));

        Assert.Empty(result);
    }
}