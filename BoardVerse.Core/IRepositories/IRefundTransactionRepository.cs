using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// BR-REFUND-01..07: Repository cho <see cref="RefundTransaction"/>.
/// </summary>
public interface IRefundTransactionRepository
{
    Task<RefundTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RefundTransaction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<RefundTransaction>> GetByReservationIdAsync(Guid reservationId, CancellationToken ct = default);
    Task<RefundTransaction> AddAsync(RefundTransaction entity, CancellationToken ct = default);
    Task UpdateAsync(RefundTransaction entity, CancellationToken ct = default);
}