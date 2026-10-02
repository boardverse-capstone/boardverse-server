using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Tồn kho ghế theo cafe × playDate × scheduled times.
/// BR-NEW-15 (2026-08-18): Dùng ScheduledStartTime/ScheduledEndTime (TimeOnly) thay vì TimeSlot.
/// </summary>
public interface ISeatInventoryRepository
{
    Task<SeatInventory?> GetAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default);

    Task<SeatInventory?> GetForUpdateAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load by FK ID (dùng cho ReleaseInventoriesAsync khi đã có SeatInventoryId).
    /// </summary>
    Task<SeatInventory?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeatInventory>> GetByCafeAsync(Guid cafeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    Task EnsureRowAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, int totalSeats, CancellationToken cancellationToken = default);

    Task AddAsync(SeatInventory seatInventory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adjust HeldSeats / InUseSeats counters atomically by delta without touching EF tracker.
    /// FIX 2026-10-02 (ReservationService ConfirmAsync DbUpdateConcurrencyException):
    /// The legacy UpdateAsync(entity) path goes through SaveChangesAsync which fires
    /// UseXminAsConcurrencyToken (xmin) check. Because the row was loaded via AsNoTracking,
    /// original xmin is never captured → WHERE xmin = 0 → 0 rows affected → concurrency
    /// exception, deterministically failing 3/3 retries.
    /// This method bypasses EF tracking entirely by running an UPDATE ... SET col = col + delta
    /// directly. Combined with FOR UPDATE lock acquired by GetForUpdateAsync earlier in the
    /// same transaction, this is race-free.
    /// </summary>
    /// <param name="id">SeatInventory.Id (primary key).</param>
    /// <param name="heldDelta">Increment to HeldSeats (may be negative; floor at 0 via GREATEST).</param>
    /// <param name="inUseDelta">Increment to InUseSeats (may be negative; floor at 0 via GREATEST).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AdjustCountersAsync(Guid id, int heldDelta, int inUseDelta, CancellationToken cancellationToken = default);

    /// <summary>
    /// DEPRECATED — has known race-condition + EF tracker bug. Use AdjustCountersAsync instead.
    /// See: ReservationService.ConfirmAsync line 875 (DbUpdateConcurrencyException after fix).
    /// </summary>
    [Obsolete("Use AdjustCountersAsync(id, heldDelta, inUseDelta, ct) — UpdateAsync triggers EF tracking path which fails with UseXminAsConcurrencyToken when the row was loaded AsNoTracking. Will be removed after migration.")]
    Task UpdateAsync(SeatInventory seatInventory, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
