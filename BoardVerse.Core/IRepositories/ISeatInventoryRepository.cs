using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Tồn kho ghế theo cafe × playDate × scheduled times.
/// BR-NEW-15 (2026-08-18): Dùng ScheduledStartTime/ScheduledEndTime (TimeOnly) thay vì TimeSlot.
/// </summary>
public interface ISeatInventoryRepository
{
    Task<SeatInventory?> GetAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime);

    Task<SeatInventory?> GetForUpdateAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime);

    /// <summary>
    /// Load by FK ID (dùng cho ReleaseInventoriesAsync khi đã có SeatInventoryId).
    /// </summary>
    Task<SeatInventory?> GetByIdForUpdateAsync(Guid id);

    Task<IReadOnlyList<SeatInventory>> GetByCafeAsync(Guid cafeId, DateOnly fromDate, DateOnly toDate);

    Task EnsureRowAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, int totalSeats);

    Task AddAsync(SeatInventory seatInventory);

    Task UpdateAsync(SeatInventory seatInventory);

    Task SaveChangesAsync();
}
