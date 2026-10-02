using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Tồn kho bản copy game theo cafe × game × playDate × scheduled times.
/// BR-NEW-15 (2026-08-18): Dùng ScheduledStartTime/ScheduledEndTime (TimeOnly) thay vì TimeSlot.
/// </summary>
public interface IGameInventoryRepository
{
    Task<GameInventory?> GetAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default);

    Task<GameInventory?> GetForUpdateAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load by FK ID (dùng cho ReleaseInventoriesAsync khi đã có GameInventoryId).
    /// </summary>
    Task<GameInventory?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    Task EnsureRowAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, int totalCopies, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adjust HeldCopies / InUseCopies counters atomically by delta without touching EF tracker.
    /// FIX 2026-10-02 (ReservationService ConfirmAsync DbUpdateConcurrencyException):
    /// Same rationale as ISeatInventoryRepository.AdjustCountersAsync. Bypasses EF tracking
    /// and UseXminAsConcurrencyToken check; uses raw UPDATE with delta arithmetic.
    /// </summary>
    /// <param name="id">GameInventory.Id (primary key).</param>
    /// <param name="heldDelta">Increment to HeldCopies (may be negative; floor at 0 via GREATEST).</param>
    /// <param name="inUseDelta">Increment to InUseCopies (may be negative; floor at 0 via GREATEST).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AdjustCountersAsync(Guid id, int heldDelta, int inUseDelta, CancellationToken cancellationToken = default);

    /// <summary>
    /// DEPRECATED — has known race-condition + EF tracker bug. Use AdjustCountersAsync instead.
    /// See: ReservationService.ConfirmAsync line 875 (DbUpdateConcurrencyException after fix).
    /// </summary>
    [Obsolete("Use AdjustCountersAsync(id, heldDelta, inUseDelta, ct) — UpdateAsync triggers EF tracking path which fails with UseXminAsConcurrencyToken when the row was loaded AsNoTracking. Will be removed after migration.")]
    Task UpdateAsync(GameInventory gameInventory, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
