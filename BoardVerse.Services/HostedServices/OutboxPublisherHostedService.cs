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
        var db = sp.GetRequiredService<BoardVerse.Data.BoardVerseDbContext>();

        // GAP-R4-A1 (cluster-safe): mở transaction TRƯỚC khi fetch batch — Postgres FOR UPDATE SKIP LOCKED
        // chỉ giữ row lock trong khi transaction còn sống. Nếu gọi SELECT ngoài transaction, lock release
        // ngay → 2 instance hosted service pick cùng batch → duplicate SignalR/FCM events.
        // Mở 1 transaction bao toàn bộ loop, commit sau khi đã mark processed/failed.
        await using var batchTx = await db.Database.BeginTransactionAsync(ct);

        var batch = await outboxRepository.FetchUnprocessedBatchAsync(BatchSize);
        if (batch.Count == 0)
        {
            await batchTx.CommitAsync(ct);
            return;
        }

        var logger = sp.GetRequiredService<ILogger<OutboxPublisherHostedService>>();

        // GAP-R6-RT-05 Fix: release row locks càng sớm càng tốt.
        // Trước đây: batchTx mở xuyên suốt loop. Trong loop, publisher.PublishAsync có thể gọi
        // FCM (HTTP) mất 5-30s/msg → batchTx giữ row lock qua HTTP call → block DB writers + extend
        // FOR UPDATE SKIP LOCKED cho 50 events × 30s = 25 phút giữ lock vô ích.
        // Fix: không giữ batchTx qua loop. PublishAsync chạy NGOÀI transaction. Mỗi event có
        // atomic claim: thử UPDATE outbox SET Processed=false với WHERE Id=... AND Processed=false
        // → atomic flip Processed=true với OptimisticLock. Nếu flip thành công → publish,
        // nếu fail → skip (đã được instance khác xử lý).
        await batchTx.CommitAsync(ct);

        foreach (var evt in batch)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            // Atomic claim: SET Processed=true WHERE Id=? AND Processed=false RETURNING Id
            // → row đã được set, nếu không flip được (race với instance khác) → skip.
            var claimed = await outboxRepository.TryClaimEventAsync(evt.Id, ct);
            if (!claimed)
            {
                logger.LogDebug(
                    "Outbox event {EventId} đã được claim bởi instance khác — skip",
                    evt.Id);
                continue;
            }

            // DLQ: nếu đã retry quá nhiều → skip + log, không block các event khác.
            if (evt.RetryCount >= MaxRetriesBeforeDLQ)
            {
                logger.LogError(
                    "Outbox event {EventId} type {EventType} đã retry {RetryCount} lần, bỏ qua (DLQ). IdempotencyKey={Key}",
                    evt.Id, evt.EventType, evt.RetryCount, evt.IdempotencyKey);
                evt.LastError = "DLQ: exceeded MaxRetriesBeforeDLQ";
                await outboxRepository.UpdateAsync(evt);
                await outboxRepository.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                await publisher.PublishAsync(evt, ct);
                // MarkProcessedAsync chỉ flip RetryCount++/LastError=null/Processed=true.
                // Race condition safe: TryClaimEventAsync đã lock rồi.
                await outboxRepository.UpdateAsync(evt);
                await outboxRepository.SaveChangesAsync(ct);
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

                await outboxRepository.MarkFailedAsync(evt, ex.Message, ct);
            }
        }
    }
}