using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho <see cref="CafeScheduleOverride"/> — cho phép cafe bật/tắt <see cref="TimeSlot"/>
/// hoặc đổi giờ mặc định từ <c>CafeSchedule</c>.
/// </summary>
public interface ICafeScheduleOverrideRepository
{
    /// <summary>
    /// Lấy override áp dụng cho (cafe, slot, playDate) — filter EffectiveFrom/EffectiveTo tại runtime.
    /// Trả null nếu không có override, caller sẽ fallback về default <c>CafeSchedule</c>.
    /// </summary>
    Task<CafeScheduleOverride?> GetActiveAsync(Guid cafeId, TimeSlot slot, DateOnly playDate);

    /// <summary>Lấy tất cả override của cafe (cho UI quản lý).</summary>
    Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId);

    Task AddAsync(CafeScheduleOverride overrideEntity);

    Task UpdateAsync(CafeScheduleOverride overrideEntity);

    /// <summary>Xóa override theo (cafe, slot) — dùng khi cafe muốn quay về default.</summary>
    Task DeleteAsync(Guid cafeId, TimeSlot slot);

    Task SaveChangesAsync();
}
