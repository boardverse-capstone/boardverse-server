using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Settings;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="LegacyBookingCleanupService"/> — quét Booking rows
/// cũ (Flow B) kẹt ở <c>PendingDeposit</c> hoặc <c>Confirmed</c> quá
/// <c>ScheduledStartTime</c> và đánh dấu <c>NoShow</c> + forfeit deposit + release table.
///
/// Bug cũ: <c>10c2d405-...</c> trên production bị kẹt 10 ngày vì không có
/// background job scan trạng thái này. Tests cover:
/// - 3 positive: PendingDeposit, Confirmed (chưa check-in), cả 2 mix.
/// - 3 negative: Confirmed đã check-in, Booking trong grace, future Booking.
/// - 1 flag-off: <c>LegacyBookingSettings.Enabled = false</c> → skip.
/// - 1 batch: CleanupBatchSize limit.
/// - 1 forfeit: deposit Paid → Forfeited.
/// - 1 release: cafeTable Reserved → Available.
/// - 1 preserve: ActiveSession khác vẫn đang giữ bàn → KHÔNG release.
/// </summary>
public class LegacyBookingCleanupServiceTests : IDisposable
{
    private readonly BoardVerseDbContext _db;

    public LegacyBookingCleanupServiceTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new BoardVerseDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RunOnceAsync_Should_MarkStalePendingDepositAsNoShow()
    {
        // Arrange: Booking PendingDeposit, ScheduledStartTime đã quá 45 phút (grace 30)
        var booking = CreateBooking(BookingStatus.PendingDeposit, scheduledStartMinutesAgo: 45);
        AddBooking(booking);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        // Act
        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        // Assert
        Assert.Equal(1, processed);
        var updated = await _db.Bookings.AsNoTracking().FirstAsync();
        Assert.Equal(BookingStatus.NoShow, updated.Status);
    }

    [Fact]
    public async Task RunOnceAsync_Should_MarkStaleUncheckedConfirmedAsNoShow()
    {
        var booking = CreateBooking(BookingStatus.Confirmed, scheduledStartMinutesAgo: 45);
        AddBooking(booking);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(1, processed);
        var updated = await _db.Bookings.AsNoTracking().FirstAsync();
        Assert.Equal(BookingStatus.NoShow, updated.Status);
    }

    [Fact]
    public async Task RunOnceAsync_Should_NotTouchCheckedInConfirmed()
    {
        var booking = CreateBooking(BookingStatus.Confirmed, scheduledStartMinutesAgo: 120);
        booking.CheckedInAt = DateTime.UtcNow.AddMinutes(-100);
        AddBooking(booking);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(0, processed);
        var unchanged = await _db.Bookings.AsNoTracking().FirstAsync();
        Assert.Equal(BookingStatus.Confirmed, unchanged.Status);
    }

    [Fact]
    public async Task RunOnceAsync_Should_NotTouchBookingWithinGrace()
    {
        var booking = CreateBooking(BookingStatus.PendingDeposit, scheduledStartMinutesAgo: 10);
        AddBooking(booking);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(0, processed);
        var unchanged = await _db.Bookings.AsNoTracking().FirstAsync();
        Assert.Equal(BookingStatus.PendingDeposit, unchanged.Status);
    }

    [Fact]
    public async Task RunOnceAsync_Should_NotTouchFutureBooking()
    {
        var booking = CreateBooking(BookingStatus.Confirmed, scheduledStartMinutesAgo: -60);
        AddBooking(booking);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(0, processed);
        var unchanged = await _db.Bookings.AsNoTracking().FirstAsync();
        Assert.Equal(BookingStatus.Confirmed, unchanged.Status);
    }

    [Fact]
    public async Task RunOnceAsync_Should_ProcessMixedCandidatesInOneTick()
    {
        var pendingStale = CreateBooking(BookingStatus.PendingDeposit, scheduledStartMinutesAgo: 60);
        var confirmedStale = CreateBooking(BookingStatus.Confirmed, scheduledStartMinutesAgo: 90);
        var confirmedFuture = CreateBooking(BookingStatus.Confirmed, scheduledStartMinutesAgo: -30);
        AddBookings(pendingStale, confirmedStale, confirmedFuture);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(2, processed);
        var all = await _db.Bookings.AsNoTracking().ToListAsync();
        Assert.Equal(2, all.Count(b => b.Status == BookingStatus.NoShow));
        Assert.Equal(1, all.Count(b => b.Status == BookingStatus.Confirmed));
    }

    [Fact]
    public async Task RunOnceAsync_Should_Skip_WhenLegacyDisabled()
    {
        var booking = CreateBooking(BookingStatus.PendingDeposit, scheduledStartMinutesAgo: 120);
        AddBooking(booking);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = false,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(0, processed);
        var unchanged = await _db.Bookings.AsNoTracking().FirstAsync();
        Assert.Equal(BookingStatus.PendingDeposit, unchanged.Status);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Skip_WhenJobExplicitlyDisabled()
    {
        var booking = CreateBooking(BookingStatus.PendingDeposit, scheduledStartMinutesAgo: 120);
        AddBooking(booking);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = false,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(0, processed);
    }

    [Fact]
    public async Task RunOnceAsync_Should_RespectCleanupBatchSize()
    {
        for (var i = 0; i < 5; i++)
        {
            AddBooking(CreateBooking(BookingStatus.PendingDeposit, scheduledStartMinutesAgo: 60));
        }
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 2
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(2, processed);
        var updatedCount = await _db.Bookings.AsNoTracking()
            .CountAsync(b => b.Status == BookingStatus.NoShow);
        Assert.Equal(2, updatedCount);
    }

    [Fact]
    public async Task RunOnceAsync_Should_BumpUpdatedAtOnProcessedBooking()
    {
        var old = DateTime.UtcNow.AddDays(-3);
        var booking = CreateBooking(BookingStatus.PendingDeposit, scheduledStartMinutesAgo: 60);
        booking.CreatedAt = old;
        booking.UpdatedAt = old;
        AddBooking(booking);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var beforeCall = DateTime.UtcNow;
        await service.RunOnceAsync(beforeCall);

        var updated = await _db.Bookings.AsNoTracking().FirstAsync();
        Assert.True(updated.UpdatedAt >= beforeCall.AddSeconds(-1),
            $"UpdatedAt phải refresh sau cleanup (got {updated.UpdatedAt}, expect >= {beforeCall.AddSeconds(-1):o})");
        Assert.Equal(BookingStatus.NoShow, updated.Status);
    }

    // ======================== BUG-2: forfeit + release tests ========================

    [Fact]
    public async Task RunOnceAsync_Should_ForfeitPaidDeposit_OnNoShow()
    {
        var booking = CreateBooking(BookingStatus.Confirmed, scheduledStartMinutesAgo: 60);
        var hostId = Guid.NewGuid();
        var deposit = CreateDeposit(booking.Id, hostId, booking.CafeId, amount: 50_000m);
        booking.BookingDeposit = deposit;
        AddBooking(booking);
        _db.BookingDeposits.Add(deposit);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(1, processed);
        var updatedDeposit = await _db.BookingDeposits.AsNoTracking().FirstAsync();
        Assert.Equal(BookingDepositStatus.Forfeited, updatedDeposit.Status);
        Assert.NotNull(updatedDeposit.ForfeitedAt);
    }

    [Fact]
    public async Task RunOnceAsync_Should_NotForfeitPendingDeposit()
    {
        var booking = CreateBooking(BookingStatus.Confirmed, scheduledStartMinutesAgo: 60);
        var hostId = Guid.NewGuid();
        var deposit = CreateDeposit(booking.Id, hostId, booking.CafeId, amount: 50_000m);
        deposit.Status = BookingDepositStatus.Pending; // chưa được Paid
        booking.BookingDeposit = deposit;
        AddBooking(booking);
        _db.BookingDeposits.Add(deposit);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        await service.RunOnceAsync(DateTime.UtcNow);

        var unchangedDeposit = await _db.BookingDeposits.AsNoTracking().FirstAsync();
        Assert.Equal(BookingDepositStatus.Pending, unchangedDeposit.Status);
        Assert.Null(unchangedDeposit.ForfeitedAt);
    }

    [Fact]
    public async Task RunOnceAsync_Should_ReleaseReservedTable_WhenNoOtherActiveSession()
    {
        var (booking, table) = await SeedConfirmedBookingWithReservedTable();
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        await service.RunOnceAsync(DateTime.UtcNow);

        var updatedTable = await _db.CafeTables.AsNoTracking().FirstAsync(t => t.Id == table.Id);
        Assert.Equal(CafeTableStatus.Available, updatedTable.Status);
    }

    [Fact]
    public async Task RunOnceAsync_Should_NotReleaseTable_WhenAnotherActiveSessionExists()
    {
        var (booking, table) = await SeedConfirmedBookingWithReservedTable();

        // Add another ActiveSession on same table, status = Active (defensive check).
        var activeSession = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = booking.CafeId,
            CafeTableId = table.Id,
            Status = GroupSessionStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ActiveSessions.Add(activeSession);
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        await service.RunOnceAsync(DateTime.UtcNow);

        var unchangedTable = await _db.CafeTables.AsNoTracking().FirstAsync(t => t.Id == table.Id);
        Assert.Equal(CafeTableStatus.Reserved, unchangedTable.Status);
    }

    [Fact]
    public async Task RunOnceAsync_Should_NotTouchAlreadyAvailableTable()
    {
        var (booking, table) = await SeedConfirmedBookingWithReservedTable();
        table.Status = CafeTableStatus.Available; // đã giải phóng trước đó
        await _db.SaveChangesAsync();

        var service = NewService(new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100
        });

        var processed = await service.RunOnceAsync(DateTime.UtcNow);

        Assert.Equal(1, processed);
        var stillAvailable = await _db.CafeTables.AsNoTracking().FirstAsync(t => t.Id == table.Id);
        Assert.Equal(CafeTableStatus.Available, stillAvailable.Status);
    }

    // ===== GAP-10: Metrics integration (admin endpoint feeds off this) =====

    [Fact]
    public async Task MetricsStore_ShouldReflectLatestRun()
    {
        var settings = new LegacyBookingSettings
        {
            Enabled = true,
            CleanupJobEnabled = true,
            PendingDepositGraceMinutes = 30,
            ConfirmedGraceMinutes = 30,
            CleanupBatchSize = 100,
        };
        var store = new LegacyBookingCleanupMetricsStore();
        var service = new LegacyBookingCleanupService(
            _db, Options.Create(settings),
            NullLogger<LegacyBookingCleanupService>.Instance,
            new BookingDepositRepository(_db),
            new CafeTableRepository(_db),
            store);

        SeedStalePendingDeposit(BookingStatus.PendingDeposit, hoursAgo: 1);
        SeedStalePendingDeposit(BookingStatus.PendingDeposit, hoursAgo: 2);
        await _db.SaveChangesAsync();

        var beforeRun = service.GetLastRunMetrics();
        Assert.Equal(0, beforeRun.TotalRuns);
        Assert.Equal(0, beforeRun.TotalBookingsProcessed);

        var processed = await service.RunOnceAsync(DateTime.UtcNow);
        Assert.Equal(2, processed);

        var afterRun = service.GetLastRunMetrics();
        Assert.Equal(1, afterRun.TotalRuns);
        Assert.Equal(2, afterRun.TotalBookingsProcessed);
        Assert.Equal(2, afterRun.LastBookingsProcessed);
        Assert.True(afterRun.LastDurationMs >= 0);
        Assert.True(afterRun.LastRunAtUtc > DateTime.MinValue);
    }

    // ======================== Helpers ========================

    private LegacyBookingCleanupService NewService(LegacyBookingSettings settings)
    {
        return new LegacyBookingCleanupService(
            _db,
            Options.Create(settings),
            NullLogger<LegacyBookingCleanupService>.Instance,
            new BookingDepositRepository(_db),
            new CafeTableRepository(_db),
            new LegacyBookingCleanupMetricsStore());
    }

    private void AddBooking(Booking booking)
    {
        SeedCafeAndTable(booking);
        _db.Bookings.Add(booking);
    }

    private void AddBookings(params Booking[] bookings)
    {
        foreach (var b in bookings)
        {
            AddBooking(b);
        }
    }

    private static Booking CreateBooking(BookingStatus status, int scheduledStartMinutesAgo)
    {
        var startTime = DateTime.UtcNow.AddMinutes(-scheduledStartMinutesAgo);
        var cafeId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        return new Booking
        {
            Id = Guid.NewGuid(),
            LobbyId = null,
            CafeId = cafeId,
            CafeTableId = tableId,
            ScheduledStartTime = startTime,
            ScheduleEndTime = startTime.AddHours(3),
            Status = status,
            VerificationQRCode = $"BV-{Guid.NewGuid():N}",
            PlayerQuantity = 4,
            CheckedInAt = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Thêm Cafe + CafeTable tối thiểu để thỏa FK cho Booking — InMemory provider yêu cầu.</summary>
    private void SeedCafeAndTable(Booking booking)
    {
        _db.Cafes.Add(new Cafe
        {
            Id = booking.CafeId,
            Name = "Test Cafe",
            ManagerId = Guid.NewGuid(),
            Address = "123 Test",
            Latitude = 0,
            Longitude = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.CafeTables.Add(new CafeTable
        {
            Id = booking.CafeTableId,
            CafeId = booking.CafeId,
            Name = "Bàn test",
            SeatCount = 4,
            Status = CafeTableStatus.Available,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Seed một Booking ở trạng thái chỉ định (thường PendingDeposit) với CreatedAt lùi về quá khứ
    /// <paramref name="hoursAgo"/> giờ — dùng để test cleanup job nhặt các booking "treo" cũ.
    /// </summary>
    private Booking SeedStalePendingDeposit(BookingStatus status, int hoursAgo)
    {
        var booking = CreateBooking(status, scheduledStartMinutesAgo: hoursAgo * 60);
        booking.CreatedAt = DateTime.UtcNow.AddHours(-hoursAgo);
        booking.UpdatedAt = booking.CreatedAt;
        SeedCafeAndTable(booking);
        _db.Bookings.Add(booking);
        return booking;
    }

    private static BookingDeposit CreateDeposit(Guid bookingId, Guid userId, Guid cafeId, decimal amount)
    {
        return new BookingDeposit
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            OrderId = $"BV-{Guid.NewGuid():N}",
            UserId = userId,
            CafeId = cafeId,
            CafeManagerId = Guid.NewGuid(),
            Amount = amount,
            Status = BookingDepositStatus.Paid,
            PaidAt = DateTime.UtcNow.AddHours(-2),
            RefundPolicy = DepositRefundPolicy.None, // forfeit không refund
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2)
        };
    }

    private async Task<(Booking booking, CafeTable table)> SeedConfirmedBookingWithReservedTable()
    {
        var cafeId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var cafe = new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            ManagerId = Guid.NewGuid(),
            Address = "123 Test",
            Latitude = 0,
            Longitude = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var table = new CafeTable
        {
            Id = tableId,
            CafeId = cafeId,
            Name = "Bàn 1",
            SeatCount = 4,
            Status = CafeTableStatus.Reserved,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var booking = CreateBooking(BookingStatus.Confirmed, scheduledStartMinutesAgo: 60);
        booking.CafeId = cafeId;
        booking.CafeTableId = tableId;
        booking.CafeTable = table;
        _db.Cafes.Add(cafe);
        _db.CafeTables.Add(table);
        _db.Bookings.Add(booking); // Skip AddBooking helper because we already seeded cafe/table above.
        await Task.CompletedTask;
        return (booking, table);
    }
}
