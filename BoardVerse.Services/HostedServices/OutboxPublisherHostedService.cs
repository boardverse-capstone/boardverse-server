using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.HostedServices;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.HostedServices;

/// <summary>
/// BR-REQUIRED §17.5: Transactional Outbox publisher.
/// Mỗi interval, quét <c>OutboxEvents</c> chưa processed, dispatch qua
/// <see cref="IOutboxEventPublisher"/>, mark processed hoặc mark failed (sẽ retry ở tick sau).
///
/// Đảm bảo:
/// - DB đã commit → event chắc chắn được publish (at-least-once).
/// - DB publish fail → chỉ retry, không mất event.
/// - Consumer xử lý idempotency theo <c>OutboxEvent.IdempotencyKey</c>.
/// </summary>
public class OutboxPublisherHostedService : PollingHostedService
{
    private const int BatchSize = 50;
    private const int MaxRetriesBeforeDLQ = 5;

    public OutboxPublisherHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPublisherHostedService> logger)
        : base(scopeFactory, logger, TimeSpan.FromSeconds(5))
    {
    }

    protected override async Task ExecuteTickAsync(IServiceProvider sp, CancellationToken ct)
    {
        var outboxRepository = sp.GetRequiredService<IOutboxRepository>();
        var publisher = sp.GetRequiredService<IOutboxEventPublisher>();

        var batch = await outboxRepository.FetchUnprocessedBatchAsync(BatchSize);
        if (batch.Count == 0)
        {
            return;
        }

        var logger = sp.GetRequiredService<ILogger<OutboxPublisherHostedService>>();

        foreach (var evt in batch)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            // DLQ: nếu đã retry quá nhiều → skip + log, không block các event khác.
            if (evt.RetryCount >= MaxRetriesBeforeDLQ)
            {
                logger.LogError(
                    "Outbox event {EventId} type {EventType} đã retry {RetryCount} lần, bỏ qua (DLQ). IdempotencyKey={Key}",
                    evt.Id, evt.EventType, evt.RetryCount, evt.IdempotencyKey);
                evt.Processed = true; // đánh dấu đã xử lý để không poll lại
                evt.LastError = "DLQ: exceeded MaxRetriesBeforeDLQ";
                await outboxRepository.MarkProcessedAsync(evt);
                continue;
            }

            try
            {
                await publisher.PublishAsync(evt, ct);
                await outboxRepository.MarkProcessedAsync(evt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Outbox event {EventId} type {EventType} publish fail. Retry count sẽ tăng. IdempotencyKey={Key}",
                    evt.Id, evt.EventType, evt.IdempotencyKey);

                await outboxRepository.MarkFailedAsync(evt, ex.Message);
            }
        }

        await outboxRepository.SaveChangesAsync();
    }
}