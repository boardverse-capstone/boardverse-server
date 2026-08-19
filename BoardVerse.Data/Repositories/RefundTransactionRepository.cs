using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class RefundTransactionRepository : IRefundTransactionRepository
{
    private readonly BoardVerseDbContext _db;

    public RefundTransactionRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<RefundTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.RefundTransactions.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<RefundTransaction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
        => _db.RefundTransactions.FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);

    public async Task<IReadOnlyList<RefundTransaction>> GetByReservationIdAsync(Guid reservationId, CancellationToken ct = default)
        => await _db.RefundTransactions
            .Where(r => r.ReservationId == reservationId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<RefundTransaction> AddAsync(RefundTransaction entity, CancellationToken ct = default)
    {
        var entry = await _db.RefundTransactions.AddAsync(entity, ct);
        await _db.SaveChangesAsync(ct);
        return entry.Entity;
    }

    public async Task UpdateAsync(RefundTransaction entity, CancellationToken ct = default)
    {
        _db.RefundTransactions.Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}