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

    Task UpdateAsync(SeatInventory seatInventory, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
