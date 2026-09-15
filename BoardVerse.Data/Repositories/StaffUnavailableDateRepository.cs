using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// StaffUnavailableDateRepository — quản lý ngày staff không thể làm việc.
/// </summary>
public class StaffUnavailableDateRepository : IStaffUnavailableDateRepository
{
    private readonly BoardVerseDbContext _db;

    public StaffUnavailableDateRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<StaffUnavailableDate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _db.StaffUnavailableDates
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffUnavailableDate>> GetByStaffAsync(
        Guid staffUserId,
        Guid cafeId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.StaffUnavailableDates
            .Where(u => u.StaffUserId == staffUserId && u.CafeId == cafeId);

        if (from.HasValue)
            query = query.Where(u => u.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(u => u.Date <= to.Value);

        return await query
            .OrderBy(u => u.Date)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsUnavailableAsync(Guid staffUserId, DateOnly date, CancellationToken cancellationToken = default)
    {
        return await _db.StaffUnavailableDates
            .AnyAsync(u => u.StaffUserId == staffUserId && u.Date == date, cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid staffUserId, DateOnly date, CancellationToken cancellationToken = default)
    {
        return await _db.StaffUnavailableDates
            .AnyAsync(u => u.StaffUserId == staffUserId && u.Date == date, cancellationToken);
    }

    public async Task AddAsync(StaffUnavailableDate item, CancellationToken cancellationToken = default)
    {
        await _db.StaffUnavailableDates.AddAsync(item, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<StaffUnavailableDate> items, CancellationToken cancellationToken = default)
    {
        await _db.StaffUnavailableDates.AddRangeAsync(items, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _db.StaffUnavailableDates.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (item != null)
        {
            _db.StaffUnavailableDates.Remove(item);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
