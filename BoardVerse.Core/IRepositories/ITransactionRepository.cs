using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task<Transaction> UpdateAsync(Transaction transaction, CancellationToken cancellationToken = default);
}
