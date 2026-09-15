using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// ShiftSwapRequestRepository — quản lý yêu cầu đổi ca giữa 2 staff.
/// </summary>
public class ShiftSwapRequestRepository : IShiftSwapRequestRepository
{
    private readonly BoardVerseDbContext _db;

    public ShiftSwapRequestRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.ShiftSwapRequests
            .Include(s => s.Requester)
            .Include(s => s.TargetStaff)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftSwapRequest>> GetByCafeAsync(Guid cafeId, ShiftSwapStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _db.ShiftSwapRequests
            .Include(s => s.Requester)
            .Include(s => s.TargetStaff)
            .Where(s => s.CafeId == cafeId);

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftSwapRequest>> GetPendingForStaffAsync(Guid staffUserId, CancellationToken cancellationToken = default)
    {
        return await _db.ShiftSwapRequests
            .Include(s => s.Requester)
            .Include(s => s.TargetStaff)
            .Where(s => s.TargetStaffId == staffUserId && s.Status == ShiftSwapStatus.Pending)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftSwapRequest>> GetByRequesterAsync(Guid requesterId, CancellationToken cancellationToken = default)
    {
        return await _db.ShiftSwapRequests
            .Include(s => s.Requester)
            .Include(s => s.TargetStaff)
            .Where(s => s.RequesterId == requesterId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ShiftSwapRequest request, CancellationToken cancellationToken = default)
    {
        await _db.ShiftSwapRequests.AddAsync(request, cancellationToken);
    }

    public async Task UpdateAsync(ShiftSwapRequest request, CancellationToken cancellationToken = default)
    {
        _db.ShiftSwapRequests.Update(request);
        await Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
