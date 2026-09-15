using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// ShiftAttendanceRepository — quản lý check-in/out ca làm việc của staff.
/// </summary>
public class ShiftAttendanceRepository : IShiftAttendanceRepository
{
    private readonly BoardVerseDbContext _db;

    public ShiftAttendanceRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<ShiftAttendance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.ShiftAttendances
            .Include(a => a.User)
            .Include(a => a.Schedule)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<ShiftAttendance?> GetByScheduleAndDateAsync(Guid scheduleId, DateOnly date, CancellationToken cancellationToken = default)
    {
        return await _db.ShiftAttendances
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.ScheduleId == scheduleId && a.Date == date, cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftAttendance>> GetByStaffAsync(
        Guid staffUserId,
        Guid cafeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        return await _db.ShiftAttendances
            .Include(a => a.Schedule)
            .Include(a => a.User)
            .Where(a => a.StaffUserId == staffUserId
                && a.Schedule!.CafeId == cafeId
                && a.Date >= startDate
                && a.Date <= endDate)
            .OrderBy(a => a.Date)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasAttendanceInRangeAsync(
        Guid staffUserId,
        Guid scheduleId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        return await _db.ShiftAttendances
            .AnyAsync(a => a.StaffUserId == staffUserId
                && a.ScheduleId == scheduleId
                && a.Date >= startDate
                && a.Date <= endDate, cancellationToken);
    }

    public async Task<int> CountByStaffAsync(Guid staffUserId, string status, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        return await _db.ShiftAttendances
            .CountAsync(a => a.StaffUserId == staffUserId
                && a.Status == status
                && a.Date >= startDate
                && a.Date <= endDate, cancellationToken);
    }

    public async Task AddAsync(ShiftAttendance attendance, CancellationToken cancellationToken = default)
    {
        await _db.ShiftAttendances.AddAsync(attendance, cancellationToken);
    }

    public async Task UpdateAsync(ShiftAttendance attendance, CancellationToken cancellationToken = default)
    {
        _db.ShiftAttendances.Update(attendance);
        await Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
