using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Tồn kho bản copy game theo cafe × game × playDate × timeSlot (§V + §19.11).
/// </summary>
public interface IGameInventoryRepository
{
    Task<GameInventory?> GetAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeSlot timeSlot);

    Task<GameInventory?> GetForUpdateAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeSlot timeSlot);

    Task EnsureRowAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeSlot timeSlot, int totalCopies);

    Task UpdateAsync(GameInventory gameInventory);

    Task SaveChangesAsync();
}