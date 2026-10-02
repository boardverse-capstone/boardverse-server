using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface IBvcTopUpRequestRepository
{
    Task<BvcTopUpRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gap 3.4: Lock BvcTopUpRequest theo Id (FOR UPDATE) để chống race condition giữa
    /// Cancel + Update Amount. Caller phải wrap transaction Serializable.
    /// Lock row level — request thứ 2 đợi đến khi request thứ 1 commit/rollback.
    /// </summary>
    Task<BvcTopUpRequest?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<BvcTopUpRequest?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<BvcTopUpRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// W-07: Lookup pending top-up by exact OrderId prefix (18 hex chars) match.
    /// Safer than 8-char hash prefix matching (avoids birthday paradox).
    /// </summary>
    Task<BvcTopUpRequest?> GetPendingByExactOrderIdAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch fetch top-up pending đã quá hạn (ExpiresAt &lt; now).
    /// Dùng FOR UPDATE SKIP LOCKED + batch transaction để cluster-safe.
    /// Caller phải wrap transaction.
    /// </summary>
    Task<IReadOnlyList<BvcTopUpRequest>> GetPendingExpiredAsync(DateTime now, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy tất cả top-up request đang Pending với AmountVnd khớp — dùng cho webhook
    /// fallback khi SePay strip dấu '-' khỏi transferContent, khiến OrderId không
    /// còn trong content. Caller tự filter theo userIdHash.
    /// </summary>
    Task<IReadOnlyList<BvcTopUpRequest>> GetPendingByAmountVndAsync(
        decimal amountVnd,
        CancellationToken cancellationToken = default);

    Task AddAsync(BvcTopUpRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(BvcTopUpRequest request, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
