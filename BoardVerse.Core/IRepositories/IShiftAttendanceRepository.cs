using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho ShiftAttendance — check-in/out ca làm việc của staff.
/// </summary>
public interface IShiftAttendanceRepository
{
    Task<ShiftAttendance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ShiftAttendance?> GetByScheduleAndDateAsync(Guid scheduleId, DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftAttendance>> GetByStaffAsync(
        Guid staffUserId,
        Guid cafeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra staff có attendance (check-in) cho schedule cụ thể trong khoảng ngày.
    /// Dùng cho CopyWeekTemplate để biết template nào đã được dùng.
    /// </summary>
    Task<bool> HasAttendanceInRangeAsync(
        Guid staffUserId,
        Guid scheduleId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<int> CountByStaffAsync(Guid staffUserId, string status, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task AddAsync(ShiftAttendance attendance, CancellationToken cancellationToken = default);
    Task UpdateAsync(ShiftAttendance attendance, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
