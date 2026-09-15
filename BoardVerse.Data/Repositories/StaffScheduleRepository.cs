using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// StaffScheduleRepository — quản lý lịch làm việc của staff.
/// </summary>
public class StaffScheduleRepository : IStaffScheduleRepository
{
    private readonly BoardVerseDbContext _db;

    public StaffScheduleRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<StaffSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.StaffSchedules
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffSchedule>> GetByCafeAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        return await _db.StaffSchedules
            .Include(s => s.User)
            .Where(s => s.CafeId == cafeId)
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffSchedule>> GetByStaffAsync(Guid staffUserId, Guid cafeId, CancellationToken cancellationToken = default)
    {
        return await _db.StaffSchedules
            .Where(s => s.StaffUserId == staffUserId && s.CafeId == cafeId)
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffSchedule>> GetByDayOfWeekAsync(Guid cafeId, DayOfWeek dayOfWeek, CancellationToken cancellationToken = default)
    {
        return await _db.StaffSchedules
            .Include(s => s.User)
            .Where(s => s.CafeId == cafeId && s.DayOfWeek == dayOfWeek)
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffSchedule>> GetActiveByCafeAsync(Guid cafeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        // Lấy tất cả lịch Active của cafe — caller sẽ filter theo ngày trong service
        return await _db.StaffSchedules
            .Include(s => s.User)
            .Where(s => s.CafeId == cafeId && s.Status == StaffScheduleStatus.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasOverlappingScheduleAsync(
        Guid cafeId,
        Guid staffUserId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default)
    {
        // Lấy tất cả lịch active của staff trong cùng DayOfWeek
        var existing = await _db.StaffSchedules
            .Where(s => s.CafeId == cafeId
                && s.StaffUserId == staffUserId
                && s.DayOfWeek == dayOfWeek
                && s.Status == StaffScheduleStatus.Active
                && (excludeScheduleId == null || s.Id != excludeScheduleId.Value))
            .ToListAsync(cancellationToken);

        // Kiểm tra overlap — xử lý cả ca qua đêm
        foreach (var s in existing)
        {
            if (DoShiftsOverlap(startTime, endTime, s.StartTime, s.EndTime))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Kiểm tra 2 ca có overlap không — xử lý ca qua đêm (EndTime &lt; StartTime).
    /// </summary>
    private static bool DoShiftsOverlap(TimeOnly s1Start, TimeOnly s1End, TimeOnly s2Start, TimeOnly s2End)
    {
        var isOvernight1 = s1End < s1Start;
        var isOvernight2 = s2End < s2Start;

        if (!isOvernight1 && !isOvernight2)
        {
            // Cả 2 ca thường — overlap nếu [s1Start, s1End) và [s2Start, s2End) giao nhau
            return s1Start < s2End && s2Start < s1End;
        }

        if (isOvernight1 && isOvernight2)
        {
            // Cả 2 ca qua đêm — luôn overlap nếu trùng DayOfWeek
            return true;
        }

        // 1 ca qua đêm, 1 ca thường — chia case
        // Ca qua đêm = [s1Start, 24:00) + [00:00, s1End)
        if (isOvernight1)
        {
            // Ca 1 qua đêm: [s1Start, 24) ∪ [0, s1End)
            // Ca 2 thường: [s2Start, s2End)
            // Overlap nếu s2Start >= s1Start (ca 2 đè lên phần tối của ca 1)
            // HOẶC s2End <= s1End (ca 2 đè lên phần sáng của ca 1)
            return s2Start >= s1Start || s2End <= s1End;
        }

        // Ca 2 qua đêm
        return s1Start >= s2Start || s1End <= s2End;
    }

    public async Task AddAsync(StaffSchedule schedule, CancellationToken cancellationToken = default)
    {
        await _db.StaffSchedules.AddAsync(schedule, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<StaffSchedule> schedules, CancellationToken cancellationToken = default)
    {
        await _db.StaffSchedules.AddRangeAsync(schedules, cancellationToken);
    }

    public async Task UpdateAsync(StaffSchedule schedule, CancellationToken cancellationToken = default)
    {
        _db.StaffSchedules.Update(schedule);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await _db.StaffSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule != null)
        {
            _db.StaffSchedules.Remove(schedule);
        }
    }

    public async Task<int> DeleteByStaffAsync(Guid cafeId, Guid staffUserId, CancellationToken cancellationToken = default)
    {
        var schedules = await _db.StaffSchedules
            .Where(s => s.CafeId == cafeId && s.StaffUserId == staffUserId)
            .ToListAsync(cancellationToken);
        _db.StaffSchedules.RemoveRange(schedules);
        return schedules.Count;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
