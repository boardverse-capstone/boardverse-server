using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public interface ICafeScheduleOverrideRepository
{
    /// <summary>
    /// Lấy override cho (cafe, applyDate).
    /// </summary>
    Task<CafeScheduleOverride?> GetByApplyDateAsync(Guid cafeId, DateOnly applyDate);

    /// <summary>Lấy tất cả override của cafe.</summary>
    Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId);

    Task AddAsync(CafeScheduleOverride overrideEntity);
    Task UpdateAsync(CafeScheduleOverride overrideEntity);
    Task DeleteByIdAsync(Guid overrideId);

    Task SaveChangesAsync();
}
