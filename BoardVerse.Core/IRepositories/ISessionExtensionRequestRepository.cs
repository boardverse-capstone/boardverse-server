using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// GAP-1 Fix: Repository cho SessionExtensionRequest.
/// </summary>
public interface ISessionExtensionRequestRepository
{
    Task<SessionExtensionRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SessionExtensionRequest?> GetByIdWithSessionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionExtensionRequest>> GetPendingBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionExtensionRequest>> GetPendingByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GAP-9 Fix: Lấy tất cả extension request (mọi status) của 1 session.
    /// Dùng cho GetCurrentSessionAsync trả LastExtensionRequest cho player.
    /// </summary>
    Task<IReadOnlyList<SessionExtensionRequest>> GetAllBySessionIdAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// GAP-7 Fix: Lấy tất cả Pending request (dùng cho background job expiry).
    /// </summary>
    Task<IReadOnlyList<SessionExtensionRequest>> GetAllPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// GAP-13 Fix: Lấy expired requests với batch limit và cutoff time.
    /// </summary>
    Task<IReadOnlyList<SessionExtensionRequest>> GetExpiredRequestsBatchAsync(DateTime cutoff, int batchSize, CancellationToken ct = default);

    /// <summary>
    /// GAP-R2-05 Fix: Atomic batch update — đánh Expired cho nhiều request cùng lúc.
    /// Dùng ExecuteUpdateAsync với WHERE Status=Pending AND CreatedAt < cutoff
    /// để tránh race condition với staff approve/reject giữa chừng.
    /// </summary>
    /// <returns>Số rows đã update thành công.</returns>
    Task<int> ExpireBatchAsync(DateTime cutoff, int batchSize, CancellationToken ct = default);

    Task AddAsync(SessionExtensionRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(SessionExtensionRequest request, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
