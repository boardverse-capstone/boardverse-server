using BoardVerse.Core.Entities;

using System.Threading;
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
    Task<CafeScheduleOverride?> GetByApplyDateAsync(Guid cafeId, DateOnly applyDate, CancellationToken cancellationToken = default);

    /// <summary>Lấy tất cả override của cafe.</summary>
    Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId, CancellationToken cancellationToken = default);

    Task AddAsync(CafeScheduleOverride overrideEntity, CancellationToken cancellationToken = default);
    Task UpdateAsync(CafeScheduleOverride overrideEntity, CancellationToken cancellationToken = default);
    Task DeleteByIdAsync(Guid overrideId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
