using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Settings;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services;

/// <summary>
/// Dọn dẹp các Booking rows legacy (Flow B) quá hạn mà bị kẹt ở
/// <see cref="BookingStatus.PendingDeposit"/> hoặc <see cref="BookingStatus.Confirmed"/>
/// quá <c>ScheduledStartTime</c>.
/// Đây là job "vacuüm cleaner" cho dữ liệu cũ — Flow A (Reservation/BVC) có
/// <see cref="ReservationNoShowDetectionJob"/> riêng, đã chạy đúng.
///
/// BUG-2 fix: Ngoài đổi status → NoShow, cần forfeit <see cref="BookingDeposit"/>
/// (chuyển về BoardVerse theo BR-18) và release <see cref="CafeTable"/> về Available.
/// Nếu không:
///   - Tiền cọc bị "treo" ở <c>Paid</c> mãi mãi.
///   - Bàn kẹt ở <c>Reserved</c> hoặc <c>InUse</c> mãi mãi.
/// </summary>
public class LegacyBookingCleanupService
{
    private readonly BoardVerseDbContext _db;
    private readonly LegacyBookingSettings _settings;
    private readonly ILogger<LegacyBookingCleanupService> _logger;
    private readonly IBookingDepositRepository _depositRepository;
    private readonly ICafeTableRepository _cafeTableRepository;
    private readonly LegacyBookingCleanupMetricsStore _metricsStore;

    /// <summary>Snapshot của lần chạy gần nhất — dùng cho `GET /api/v1/admin/legacy-booking/cleanup-stats`.</summary>
    public LegacyBookingCleanupMetrics GetLastRunMetrics() => _metricsStore.Snapshot();

    public LegacyBookingCleanupService(
        BoardVerseDbContext db,
        IOptions<LegacyBookingSettings> settings,
        ILogger<LegacyBookingCleanupService> logger,
        IBookingDepositRepository depositRepository,
        ICafeTableRepository cafeTableRepository,
        LegacyBookingCleanupMetricsStore metricsStore)
    {
        _db = db;
        _settings = settings.Value;
        _logger = logger;
        _depositRepository = depositRepository;
        _cafeTableRepository = cafeTableRepository;
        _metricsStore = metricsStore;
    }

    /// <summary>
    /// Chạy 1 tick cleanup. Trả về số row đã cập nhật (0 nếu không có candidate).
    /// </summary>
    public async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (!_settings.Enabled || !_settings.CleanupJobEnabled)
        {
            _logger.LogDebug("LegacyBookingCleanupService skipped (Enabled={Enabled}, Job={Job})",
                _settings.Enabled, _settings.CleanupJobEnabled);
            return 0;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var pendingCutoff = nowUtc.AddMinutes(-_settings.PendingDepositGraceMinutes);
        var confirmedCutoff = nowUtc.AddMinutes(-_settings.ConfirmedGraceMinutes);

        // Tách 2 query: (1) lấy IDs theo filter, (2) load navigation.
        // Lưu ý: Booking.UpdatedAt là concurrency token — không nên dùng Include() + SaveChanges
        // trong cùng 1 query vì InMemory provider có thể conflict.
        var candidateIds = await _db.Bookings
            .Where(b =>
                (b.Status == BookingStatus.PendingDeposit
                    && b.ScheduledStartTime <= pendingCutoff)
                ||
                (b.Status == BookingStatus.Confirmed
                    && b.ScheduledStartTime <= confirmedCutoff
                    && b.CheckedInAt == null))
            .OrderBy(b => b.ScheduledStartTime)
            .Take(_settings.CleanupBatchSize)
            .Select(b => b.Id)
            .ToListAsync(ct);

        _logger.LogDebug("LegacyBookingCleanupService found {Count} candidate IDs", candidateIds.Count);

        if (candidateIds.Count == 0)
        {
            return 0;
        }

        var candidates = await _db.Bookings
            .Include(b => b.BookingDeposit)
            .Include(b => b.CafeTable)
            .Where(b => candidateIds.Contains(b.Id))
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var forfeitedDeposits = 0;
        var releasedTables = 0;

        foreach (var booking in candidates)
        {
            // 1. Forfeit BookingDeposit (nếu có) — chuyển từ Paid → Forfeited theo BR-18.
            //    Confirmed + có deposit = khách đã cọc nhưng không đến → forfeit về BoardVerse.
            //    PendingDeposit thường không có deposit Paid (chưa ai cọc), nhưng defensive check anyway.
            if (booking.BookingDeposit != null
                && booking.BookingDeposit.Status == BookingDepositStatus.Paid)
            {
                booking.BookingDeposit.Status = BookingDepositStatus.Forfeited;
                booking.BookingDeposit.ForfeitedAt = nowUtc;
                booking.BookingDeposit.UpdatedAt = nowUtc;
                forfeitedDeposits++;
            }

            // 2. Release CafeTable (nếu đang Reserved/InUse và không có ActiveSession khác).
            //    Copy logic từ BookingService.CancelBookingAsync — defensive check ActiveSession.
            if (booking.CafeTable != null
                && (booking.CafeTable.Status == CafeTableStatus.Reserved
                    || booking.CafeTable.Status == CafeTableStatus.InUse))
            {
                var hasActiveSession = await _db.ActiveSessions
                    .AsNoTracking()
                    .AnyAsync(s => s.CafeTableId == booking.CafeTableId
                                   && s.CafeId == booking.CafeId
                                   && (s.Status == GroupSessionStatus.Active
                                       || s.Status == GroupSessionStatus.Checking
                                       || s.Status == GroupSessionStatus.Unpaid), ct);

                if (!hasActiveSession)
                {
                    booking.CafeTable.Status = CafeTableStatus.Available;
                    booking.CafeTable.UpdatedAt = nowUtc;
                    await _cafeTableRepository.UpdateAsync(booking.CafeTable);
                    releasedTables++;
                }
            }

            // 3. Đổi Booking status → NoShow (matches NoShow detection).
            booking.Status = BookingStatus.NoShow;
            booking.UpdatedAt = nowUtc;
        }

        await _db.SaveChangesAsync(ct);

        stopwatch.Stop();

        // ===== GAP-10: cập nhật metrics cho admin endpoint =====
        _metricsStore.Record(nowUtc, candidates.Count, forfeitedDeposits, releasedTables, stopwatch.ElapsedMilliseconds);

        _logger.LogInformation(
            "LegacyBookingCleanupService marked {Count} stale Bookings as NoShow " +
            "(forfeitedDeposits={ForfeitedDeposits}, releasedTables={ReleasedTables}, " +
            "PendingCutoff={PendingCutoff}, ConfirmedCutoff={ConfirmedCutoff}, DurationMs={DurationMs})",
            candidates.Count, forfeitedDeposits, releasedTables, pendingCutoff, confirmedCutoff, stopwatch.ElapsedMilliseconds);

        return candidates.Count;
    }
}

/// <summary>
/// Snapshot metrics của LegacyBookingCleanupService — exposed qua admin endpoint.
/// </summary>
public class LegacyBookingCleanupMetrics
{
    public DateTime LastRunAtUtc { get; set; }
    public int LastBookingsProcessed { get; set; }
    public int LastDepositsForfeited { get; set; }
    public int LastTablesReleased { get; set; }
    public long TotalRuns { get; set; }
    public long TotalBookingsProcessed { get; set; }
    public long LastDurationMs { get; set; }
}

/// <summary>
/// Singleton metrics store cho <see cref="LegacyBookingCleanupService"/>. Persist data
/// across scoped requests — background job (every 5 min) writes; admin endpoint reads.
/// </summary>
public class LegacyBookingCleanupMetricsStore
{
    private readonly object _lock = new();
    private LegacyBookingCleanupMetrics _current = new();

    public void Record(DateTime nowUtc, int bookingsProcessed, int depositsForfeited, int tablesReleased, long durationMs)
    {
        lock (_lock)
        {
            _current = new LegacyBookingCleanupMetrics
            {
                LastRunAtUtc = nowUtc,
                LastBookingsProcessed = bookingsProcessed,
                LastDepositsForfeited = depositsForfeited,
                LastTablesReleased = tablesReleased,
                TotalRuns = _current.TotalRuns + 1,
                TotalBookingsProcessed = _current.TotalBookingsProcessed + bookingsProcessed,
                LastDurationMs = durationMs,
            };
        }
    }

    public LegacyBookingCleanupMetrics Snapshot()
    {
        lock (_lock)
        {
            return new LegacyBookingCleanupMetrics
            {
                LastRunAtUtc = _current.LastRunAtUtc,
                LastBookingsProcessed = _current.LastBookingsProcessed,
                LastDepositsForfeited = _current.LastDepositsForfeited,
                LastTablesReleased = _current.LastTablesReleased,
                TotalRuns = _current.TotalRuns,
                TotalBookingsProcessed = _current.TotalBookingsProcessed,
                LastDurationMs = _current.LastDurationMs,
            };
        }
    }
}
