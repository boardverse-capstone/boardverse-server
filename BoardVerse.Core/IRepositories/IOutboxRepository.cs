using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Transactional Outbox repository (BR-REQUIRED §17.5).
/// Add vào cùng transaction với domain mutation; background worker đọc processed = false.
/// </summary>
public interface IOutboxRepository
{
    Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default);

    /// <summary>Lấy tối đa <paramref name="batchSize"/> event chưa xử lý, sắp xếp theo CreatedAt (oldest first).</summary>
    Task<IReadOnlyList<OutboxEvent>> FetchUnprocessedBatchAsync(int batchSize, CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(OutboxEvent outboxEvent, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// GAP-R6-RT-05: Atomic claim event bằng UPDATE ... WHERE Processed = false + NextRetryAt &lt;= now.
    /// Trả về true nếu claim được (chỉ 1 worker claim được), false nếu event đã được claim bởi worker khác.
    /// </summary>
    Task<bool> TryClaimEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GAP-R6-RT-05: Update event (re-attach + mark modified). Dùng cho UpdateProcessed/UpdateFailed trong worker.
    /// </summary>
    Task UpdateAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default);
    Task<int> DeleteProcessedOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}