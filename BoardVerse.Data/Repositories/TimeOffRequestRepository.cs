using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// TimeOffRequestRepository — quản lý yêu cầu nghỉ phép của staff.
/// </summary>
public class TimeOffRequestRepository : ITimeOffRequestRepository
{
    private readonly BoardVerseDbContext _db;

    public TimeOffRequestRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<TimeOffRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.TimeOffRequests
            .Include(t => t.Staff)
            .Include(t => t.ReviewedByUser)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<TimeOffRequest>> GetByCafeAsync(Guid cafeId, TimeOffStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _db.TimeOffRequests
            .Include(t => t.Staff)
            .Include(t => t.ReviewedByUser)
            .Where(t => t.CafeId == cafeId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TimeOffRequest>> GetByStaffAsync(Guid staffUserId, CancellationToken cancellationToken = default)
    {
        return await _db.TimeOffRequests
            .Include(t => t.ReviewedByUser)
            .Where(t => t.StaffUserId == staffUserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(TimeOffRequest request, CancellationToken cancellationToken = default)
    {
        await _db.TimeOffRequests.AddAsync(request, cancellationToken);
    }

    public async Task UpdateAsync(TimeOffRequest request, CancellationToken cancellationToken = default)
    {
        _db.TimeOffRequests.Update(request);
        await Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
