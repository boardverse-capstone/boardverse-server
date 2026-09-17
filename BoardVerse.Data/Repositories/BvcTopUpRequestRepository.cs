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

    public async Task<IReadOnlyList<BvcTopUpRequest>> GetPendingExpiredAsync(DateTime now, int limit = 50, CancellationToken cancellationToken = default)
    {
        // Cluster-safe: FOR UPDATE SKIP LOCKED + push filter xuống SQL.
        // Caller wrap batch transaction.
        // Fix (42883): Dùng ExecuteSqlInterpolated giữ compile-time type → Npgsql gửi INTEGER.
        return await _db.BvcTopUpRequests
            .FromSqlInterpolated(
                $@"SELECT * FROM ""BvcTopUpRequests""
                   WHERE ""Status"" = {(int)BvcTopUpStatus.Pending}
                     AND ""ExpiresAt"" <= {now}
                   ORDER BY ""ExpiresAt""
                   LIMIT {limit}
                   FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
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

    public async Task AddAsync(BvcTopUpRequest request, CancellationToken cancellationToken = default)
    {
        await _db.BvcTopUpRequests.AddAsync(request);
    }

    /// <summary>
    /// W-07: Lookup pending top-up by exact OrderId prefix (18 hex chars) match.
    /// Safer than 8-char hash prefix matching.
    /// </summary>
    public async Task<BvcTopUpRequest?> GetPendingByExactOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        return await _db.BvcTopUpRequests
            .FirstOrDefaultAsync(r => r.OrderId == orderId && r.Status == BvcTopUpStatus.Pending, cancellationToken);
    }

    public Task UpdateAsync(BvcTopUpRequest request, CancellationToken cancellationToken = default)
    {
        _db.BvcTopUpRequests.Update(request);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
