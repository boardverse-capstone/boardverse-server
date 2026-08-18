using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Tồn kho bản copy game theo cafe × game × playDate × scheduled times.
/// BR-NEW-15 (2026-08-18): Dùng ScheduledStartTime/ScheduledEndTime (TimeOnly) thay vì TimeSlot.
/// </summary>
public interface IGameInventoryRepository
{
    Task<GameInventory?> GetAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime);

    Task<GameInventory?> GetForUpdateAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime);

    /// <summary>
    /// Load by FK ID (dùng cho ReleaseInventoriesAsync khi đã có GameInventoryId).
    /// </summary>
    Task<GameInventory?> GetByIdForUpdateAsync(Guid id);

    Task EnsureRowAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, int totalCopies);

    Task UpdateAsync(GameInventory gameInventory);

    Task SaveChangesAsync();
}
