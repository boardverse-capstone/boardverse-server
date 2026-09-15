using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho StaffSchedule — lịch làm việc của staff theo ngày trong tuần.
/// </summary>
public interface IStaffScheduleRepository
{
    Task<StaffSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaffSchedule>> GetByCafeAsync(Guid cafeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaffSchedule>> GetByStaffAsync(Guid staffUserId, Guid cafeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaffSchedule>> GetByDayOfWeekAsync(Guid cafeId, DayOfWeek dayOfWeek, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy lịch cho 1 khoảng ngày — duyệt qua từng ngày và map theo DayOfWeek.
    /// </summary>
    Task<IReadOnlyList<StaffSchedule>> GetActiveByCafeAsync(Guid cafeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra xung đột: staff này đã có lịch nào overlap với (startTime, endTime) trong cùng DayOfWeek chưa?
    /// Hỗ trợ ca qua đêm (EndTime &lt; StartTime → ca qua 2 ngày, kiểm tra cả 2).
    /// </summary>
    Task<bool> HasOverlappingScheduleAsync(
        Guid cafeId,
        Guid staffUserId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(StaffSchedule schedule, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<StaffSchedule> schedules, CancellationToken cancellationToken = default);
    Task UpdateAsync(StaffSchedule schedule, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> DeleteByStaffAsync(Guid cafeId, Guid staffUserId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
