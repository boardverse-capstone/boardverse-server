using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task<Transaction> UpdateAsync(Transaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bắt đầu DB transaction. Caller chịu trách nhiệm Commit/Rollback.
    /// Dùng khi cần atomicity giữa Transaction + các entity khác.
    /// </summary>
    Task<IDatabaseTransactionContext> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
