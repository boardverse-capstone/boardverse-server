using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class BvcRefundRequestRepository : IBvcRefundRequestRepository
{
    private readonly BoardVerseDbContext _db;

    public BvcRefundRequestRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<BvcRefundRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _db.BvcRefundRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public Task<BvcRefundRequest?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return _db.BvcRefundRequests.FirstOrDefaultAsync(
            r => r.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public Task<BvcRefundRequest?> GetByIdWithLedgerEntryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _db.BvcRefundRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public Task<BvcRefundRequest?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _db.BvcRefundRequests
            .FromSqlInterpolated($"SELECT * FROM \"BvcRefundRequests\" WHERE \"Id\" = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<BvcRefundRequest> Items, int TotalCount)> GetPagedAsync(
        RefundRequestStatus? statusFilter,
        Guid? userIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.BvcRefundRequests.AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(r => r.Status == statusFilter.Value);
        }

        if (userIdFilter.HasValue)
        {
            query = query.Where(r => r.UserId == userIdFilter.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<BvcRefundRequest> Items, int TotalCount)> GetByUserIdPagedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.BvcRefundRequests.Where(r => r.UserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(BvcRefundRequest request, CancellationToken cancellationToken = default)
    {
        await _db.BvcRefundRequests.AddAsync(request, cancellationToken);
    }

    public Task UpdateAsync(BvcRefundRequest request)
    {
        request.UpdatedAt = DateTime.UtcNow;
        _db.BvcRefundRequests.Update(request);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}