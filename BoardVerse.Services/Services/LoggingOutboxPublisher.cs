using BoardVerse.Core.Entities;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Default no-op implementation của <see cref="IOutboxEventPublisher"/>.
/// MVP: chỉ log event. Sau này sẽ thay bằng SignalR + push notification dispatcher.
/// </summary>
public class LoggingOutboxPublisher : IOutboxEventPublisher
{
    private readonly ILogger<LoggingOutboxPublisher> _logger;

    public LoggingOutboxPublisher(ILogger<LoggingOutboxPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Outbox] publish {EventType} for {Subject}. IdempotencyKey={Key}, LobbyId={LobbyId}, ReservationId={ReservationId}",
            outboxEvent.EventType,
            outboxEvent.UserId,
            outboxEvent.IdempotencyKey,
            outboxEvent.LobbyId,
            outboxEvent.ReservationId);

        return Task.CompletedTask;
    }
}