using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

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
}
