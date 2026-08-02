using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Tồn kho ghế theo cafe × playDate × timeSlot (§V + §19.11).
/// Dùng cho atomic reservation (BR §17.3 / 17.4).
/// </summary>
public interface ISeatInventoryRepository
{
    Task<SeatInventory?> GetAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot);

    Task<SeatInventory?> GetForUpdateAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot);

    Task<IReadOnlyList<SeatInventory>> GetByCafeAsync(Guid cafeId, DateOnly fromDate, DateOnly toDate);

    Task EnsureRowAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot, int totalSeats);

    Task AddAsync(SeatInventory seatInventory);

    Task UpdateAsync(SeatInventory seatInventory);

    Task SaveChangesAsync();
}