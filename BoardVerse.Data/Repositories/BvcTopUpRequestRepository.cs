using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class BvcTopUpRequestRepository : IBvcTopUpRequestRepository
{
    private readonly BoardVerseDbContext _db;

    public BvcTopUpRequestRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<BvcTopUpRequest?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        return _db.BvcTopUpRequests
            .FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
    }

    public Task<BvcTopUpRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _db.BvcTopUpRequests
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public Task<BvcTopUpRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return _db.BvcTopUpRequests
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<IReadOnlyList<BvcTopUpRequest>> GetPendingExpiredAsync(DateTime now, int limit = 50)
    {
        // Cluster-safe: FOR UPDATE SKIP LOCKED + push filter xuống SQL.
        // Caller wrap batch transaction.
        return await _db.BvcTopUpRequests
            .FromSqlRaw(
                "SELECT * FROM \"BvcTopUpRequests\" " +
                "WHERE \"Status\" = {0} AND \"ExpiresAt\" <= {1} " +
                "ORDER BY \"ExpiresAt\" " +
                "LIMIT {2} " +
                "FOR UPDATE SKIP LOCKED",
                (int)BvcTopUpStatus.Pending, now, limit)
            .ToListAsync();
    }

    public Task<IReadOnlyList<BvcTopUpRequest>> GetPendingByAmountVndAsync(
        decimal amountVnd,
        CancellationToken cancellationToken = default)
    {
        // Lookup read-only (không lock) để tìm candidate OrderId cho webhook fallback.
        // Filter Status=Pending + AmountVnd match. Caller tự filter thêm theo userIdHash.
        return _db.BvcTopUpRequests
            .Where(r => r.Status == BvcTopUpStatus.Pending && r.AmountVnd == amountVnd)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ContinueWith<IReadOnlyList<BvcTopUpRequest>>(
                t => t.Result,
                cancellationToken,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
    }

    public async Task AddAsync(BvcTopUpRequest request)
    {
        await _db.BvcTopUpRequests.AddAsync(request);
    }

    public Task UpdateAsync(BvcTopUpRequest request)
    {
        _db.BvcTopUpRequests.Update(request);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
