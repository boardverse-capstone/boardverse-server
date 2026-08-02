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
