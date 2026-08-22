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

    Task UpdateAsync(GameInventory gameInventory, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
