using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Transactional Outbox repository (BR-REQUIRED §17.5).
/// Add vào cùng transaction với domain mutation; background worker đọc processed = false.
/// </summary>
public interface IOutboxRepository
{
    Task AddAsync(OutboxEvent outboxEvent);

    /// <summary>Lấy tối đa <paramref name="batchSize"/> event chưa xử lý, sắp xếp theo CreatedAt (oldest first).</summary>
    Task<IReadOnlyList<OutboxEvent>> FetchUnprocessedBatchAsync(int batchSize);

    Task MarkProcessedAsync(OutboxEvent outboxEvent);

    Task MarkFailedAsync(OutboxEvent outboxEvent, string errorMessage);

    Task SaveChangesAsync();
}