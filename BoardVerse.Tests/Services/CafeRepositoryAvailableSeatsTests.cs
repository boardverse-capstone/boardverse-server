using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data.Repositories;
using BoardVerse.Tests.Helpers;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho CafeRepository.GetAvailableSeatsByTimeSlotAsync — Gap G3.
/// BR §17.3: khi SeatInventory row không tồn tại, fallback phải trừ
/// Reservation HeldSeats + ActiveSession InUseSeats thay vì trả TotalSeats thô.
///
/// BR-NEW-15 (2026-08-18): SeatInventory dùng ScheduledStartTime/ScheduledEndTime (TimeOnly)
/// thay vì TimeSlot enum. Repository GetAvailableSeatsByTimeSlotAsync vẫn dùng TimeSlot
/// để trả về Dictionary&lt;TimeSlot, int&gt; (backward compat), nhưng query SeatInventory
/// bằng ScheduledStartTime/ScheduledEndTime matching TimeSlot defaults.
/// </summary>
public class CafeRepositoryAvailableSeatsTests : IDisposable
{
    private readonly FakeDbContext _db;
    private readonly CafeRepository _repo;
    private readonly Guid _cafeId = Guid.NewGuid();

    public CafeRepositoryAvailableSeatsTests()
    {
        _db = new FakeDbContext();
        _repo = new CafeRepository(_db);

        // Seed manager user trước (FK từ Cafe.ManagerId).
        // Email phải unique trên DB dùng chung (FakeDbContext dùng Postgres thật
        // khi có DATABASE_URL) → thêm GUID để tránh conflict.
        var managerId = Guid.NewGuid();
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
            ManagerId = managerId
        });
        _db.SaveChanges();

        // Seed game + host (FK cho Reservation)
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

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedReservation(
        DateOnly playDate,
        TimeSlot timeSlot,
        int maxPlayers,
        ReservationStatus status)
    {
        var uniqueId = Guid.NewGuid();
        var reservation = new Reservation
        {
            Id = uniqueId,
            HostId = _hostId,
            CafeId = _cafeId,
            GameId = _gameId,
            PlayDate = playDate,
            TimeSlot = timeSlot,
            ScheduledStartTime = playDate.ToDateTime(new TimeOnly(13, 0)),
            ScheduledEndTime = playDate.ToDateTime(new TimeOnly(15, 0)),
            RecruitmentDeadline = playDate.ToDateTime(new TimeOnly(11, 0)),
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
    }

    private void SeedActiveSession(
        DateTime startedAt,
        GroupSessionStatus status,
        int memberCount)
    {
        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = _cafeId,
            CafeTableId = null,
            Status = status,
            StartedAt = startedAt,
            EndedAt = null,
            PaidAt = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Members = new List<ActiveSessionMember>()
        };
        for (int i = 0; i < memberCount; i++)
        {
            session.Members.Add(new ActiveSessionMember
            {
                Id = Guid.NewGuid(),
                ActiveSessionId = session.Id,
                UserId = Guid.NewGuid(),
                Status = IndividualSessionStatus.Playing,
                JoinedAt = startedAt
            });
        }
        _db.ActiveSessions.Add(session);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetAvailableSeatsByTimeSlotAsync_Should_ReturnSeatInventoryValues_WhenExists()
    {
        // Happy path: SeatInventory tồn tại → dùng AvailableSeats computed.
        // BR-NEW-15: SeatInventory dùng ScheduledStartTime/ScheduledEndTime
        // matching TimeSlot defaults để query compatibility.
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        _db.SeatInventories.Add(new SeatInventory
        {
            Id = Guid.NewGuid(),
            CafeId = _cafeId,
            PlayDate = playDate,
            ScheduledStartTime = TimeSlot.Afternoon.GetStartTime(), // 12:00
            ScheduledEndTime = TimeSlot.Afternoon.GetEndTime(),     // 17:00
            TotalSeats = 20,
            HeldSeats = 4,
            InUseSeats = 2,
            RowVersion = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var result = await _repo.GetAvailableSeatsByTimeSlotAsync(_cafeId, playDate);

        Assert.Equal(14, result[TimeSlot.Afternoon]); // 20 - 4 - 2
    }

    [Fact]
    public async Task GetAvailableSeatsByTimeSlotAsync_Should_SubtractReservationHeld_WhenInventoryMissing()
    {
        // Gap G3 fix: khi KHÔNG có SeatInventory row, fallback phải trừ reservation held.
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        SeedReservation(playDate, TimeSlot.Afternoon, 5, ReservationStatus.Holding);
        SeedReservation(playDate, TimeSlot.Afternoon, 3, ReservationStatus.Confirmed);

        var result = await _repo.GetAvailableSeatsByTimeSlotAsync(_cafeId, playDate);

        // Total = 20, no inventory → fallback trừ 5+3=8 → 20-8 = 12
        Assert.Equal(12, result[TimeSlot.Afternoon]);
    }

    [Fact]
    public async Task GetAvailableSeatsByTimeSlotAsync_Should_IgnoreCancelledReservation_WhenInventoryMissing()
    {
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        SeedReservation(playDate, TimeSlot.Afternoon, 4, ReservationStatus.Holding);
        SeedReservation(playDate, TimeSlot.Afternoon, 3, ReservationStatus.CancelledByPlayer);
        SeedReservation(playDate, TimeSlot.Afternoon, 2, ReservationStatus.Expired);

        var result = await _repo.GetAvailableSeatsByTimeSlotAsync(_cafeId, playDate);

        // Chỉ trừ 4 (Holding). Cancelled/Expired bị ignore.
        Assert.Equal(16, result[TimeSlot.Afternoon]);
    }

    [Fact]
    public async Task GetAvailableSeatsByTimeSlotAsync_Should_OnlySubtractSameTimeSlot_WhenInventoryMissing()
    {
        // Reservation ở slot MORNING không nên ảnh hưởng AFTERNOON.
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        SeedReservation(playDate, TimeSlot.Morning, 10, ReservationStatus.Holding);
        SeedReservation(playDate, TimeSlot.Afternoon, 3, ReservationStatus.Holding);

        var result = await _repo.GetAvailableSeatsByTimeSlotAsync(_cafeId, playDate);

        // Morning: không có inventory → fallback 20-10 = 10
        Assert.Equal(10, result[TimeSlot.Morning]);
        // Afternoon: không có inventory → fallback 20-3 = 17
        Assert.Equal(17, result[TimeSlot.Afternoon]);
        // Evening: không có inventory + không có reservation → fallback 20
        Assert.Equal(20, result[TimeSlot.Evening]);
    }

    [Fact]
    public async Task GetAvailableSeatsByTimeSlotAsync_Should_ReturnZero_WhenHeldExceedsTotal()
    {
        // Edge case: tổng held > TotalSeats → Math.Max(0, ...) bảo vệ UI.
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        SeedReservation(playDate, TimeSlot.Afternoon, 25, ReservationStatus.Holding);

        var result = await _repo.GetAvailableSeatsByTimeSlotAsync(_cafeId, playDate);

        Assert.Equal(0, result[TimeSlot.Afternoon]);
    }

    [Fact]
    public async Task GetAvailableSeatsByTimeSlotAsync_Should_SubtractReservationForSpecificSlot_WhenInventoryExistsForAnother()
    {
        // Mixed: SeatInventory cho slot A tồn tại, slot B phải fallback.
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // Slot Afternoon có inventory
        _db.SeatInventories.Add(new SeatInventory
        {
            Id = Guid.NewGuid(),
            CafeId = _cafeId,
            PlayDate = playDate,
            ScheduledStartTime = TimeSlot.Afternoon.GetStartTime(),
            ScheduledEndTime = TimeSlot.Afternoon.GetEndTime(),
            TotalSeats = 20,
            HeldSeats = 0,
            InUseSeats = 0,
            RowVersion = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        // Slot Morning không có inventory + có reservation held
        SeedReservation(playDate, TimeSlot.Morning, 6, ReservationStatus.Holding);

        var result = await _repo.GetAvailableSeatsByTimeSlotAsync(_cafeId, playDate);

        // Afternoon: từ inventory → 20
        Assert.Equal(20, result[TimeSlot.Afternoon]);
        // Morning: fallback trừ 6 → 20-6 = 14
        Assert.Equal(14, result[TimeSlot.Morning]);
    }

    [Fact]
    public async Task GetAvailableSeatsByTimeSlotAsync_Should_ReturnAllFourSlots()
    {
        // 4 enum values đều phải trả kết quả (không phải chỉ slot có reservation).
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var result = await _repo.GetAvailableSeatsByTimeSlotAsync(_cafeId, playDate);

        Assert.Equal(4, result.Count);
        Assert.Contains(TimeSlot.Morning, result.Keys);
        Assert.Contains(TimeSlot.Afternoon, result.Keys);
        Assert.Contains(TimeSlot.Evening, result.Keys);
        Assert.Contains(TimeSlot.LateNight, result.Keys);
        Assert.Equal(20, result[TimeSlot.Morning]);
        Assert.Equal(20, result[TimeSlot.Afternoon]);
        Assert.Equal(20, result[TimeSlot.Evening]);
        Assert.Equal(20, result[TimeSlot.LateNight]);
    }
}
