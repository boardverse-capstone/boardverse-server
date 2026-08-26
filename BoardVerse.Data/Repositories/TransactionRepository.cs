using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// Adapter wrapping EF Core IDbContextTransaction thành IDatabaseTransactionContext của Core.
/// Tránh Core phải reference EF Core.
/// </summary>
public sealed class EfTransactionContextAdapter : IDatabaseTransactionContext
{
    private readonly IDbContextTransaction _inner;
    private bool _completed;

    public EfTransactionContextAdapter(IDbContextTransaction inner)
    {
        _inner = inner;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        _completed = true;
        return _inner.CommitAsync(cancellationToken);
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        _completed = true;
        return _inner.RollbackAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // Nếu caller quên Commit/Rollback, mặc định Rollback (an toàn hơn).
        if (!_completed)
        {
            try { await _inner.RollbackAsync(); } catch { /* swallow */ }
        }
        await _inner.DisposeAsync();
    }
}

internal class TransactionRepositoryBase
{
    protected static IDatabaseTransactionContext Wrap(IDbContextTransaction tx)
    {
        return new EfTransactionContextAdapter(tx);
    }
}

public class TransactionRepository : ITransactionRepository
{
    private readonly BoardVerseDbContext _context;

    public TransactionRepository(BoardVerseDbContext context)
    {
        _context = context;
    }

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Where(t => t.GatewayTransactionId == orderId || t.Notes.Contains(orderId))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<Transaction> UpdateAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        _context.Transactions.Update(transaction);
        await _context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<IDatabaseTransactionContext> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransactionContextAdapter(tx);
    }
}
