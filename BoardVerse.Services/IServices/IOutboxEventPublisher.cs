using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Dispatcher cho OutboxEvent ra ngoài runtime (SignalR hub, push notification, etc.).
/// Implementations phải idempotent — consumer xử lý trùng theo OutboxEvent.IdempotencyKey.
/// </summary>
public interface IOutboxEventPublisher
{
    /// <summary>
    /// Publish 1 event. Throw exception nếu fail (hosted service sẽ retry).
    /// </summary>
    Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}