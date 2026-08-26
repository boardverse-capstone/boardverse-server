using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// EF Core implementation của <see cref="IOutboxRepository"/> (BR-REQUIRED §17.5).
/// </summary>
public class OutboxRepository : IOutboxRepository
{
    private readonly BoardVerseDbContext _db;

    public OutboxRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        if (outboxEvent.Id == Guid.Empty)
        {
            outboxEvent.Id = Guid.NewGuid();
        }

        if (outboxEvent.CreatedAt == default)
        {
            outboxEvent.CreatedAt = DateTime.UtcNow;
        }

        _db.OutboxEvents.Add(outboxEvent);
        return Task.CompletedTask;
    }

/// <summary>
        /// GAP #18 fix: BR-REQUIRED §17.5 — Fetch batch với <c>FOR UPDATE SKIP LOCKED</c>.
        /// Cho phép cluster deploy nhiều instance chạy đồng thời:
        /// instance A lock 50 rows, instance B skip các row đó, lock 50 rows khác → không duplicate publish.
        /// Postgres-specific (các DB khác cần custom lock syntax).
        ///
        /// GAP-R4-A8 Fix: Thêm filter <c>NextRetryAt IS NULL OR NextRetryAt &lt;= now()</c>
        /// để exponential backoff hoạt động — poison event không spam mỗi tick.
        /// </summary>
        public async Task<IReadOnlyList<OutboxEvent>> FetchUnprocessedBatchAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0)
            {
                batchSize = 50;
            }

            // Lock rows không có ai đang giữ → cluster-safe.
            // batchSize đã validate (1..50), nối literal trực tiếp, không SQL injection risk.
            var sql = $"SELECT * FROM \"OutboxEvents\" WHERE \"Processed\" = false " +
                      $"AND (\"NextRetryAt\" IS NULL OR \"NextRetryAt\" <= NOW() AT TIME ZONE 'UTC') " +
                      $"ORDER BY \"CreatedAt\" ASC LIMIT {batchSize} FOR UPDATE SKIP LOCKED";

            return await _db.OutboxEvents
                .FromSqlRaw(sql)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// GAP-R6-RT-05 Fix: atomic claim 1 OutboxEvent cho instance này.
        /// UPDATE WHERE Processed=false SET Processed=true RETURNING Id → race-safe.
        /// </summary>
        public async Task<bool> TryClaimEventAsync(Guid eventId, CancellationToken cancellationToken)
        {
            var rowsAffected = await _db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "OutboxEvents"
                SET "Processed" = true, "ProcessedAt" = NOW() AT TIME ZONE 'UTC'
                WHERE "Id" = {0} AND "Processed" = false
                """,
                [eventId],
                cancellationToken);
            return rowsAffected > 0;
        }

        public Task MarkProcessedAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
        {
            // Re-attach (vì đã AsNoTracking ở fetch) rồi update.
            _db.OutboxEvents.Attach(outboxEvent);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAt = DateTime.UtcNow;
            outboxEvent.LastError = null;
            outboxEvent.NextRetryAt = null;
            _db.Entry(outboxEvent).Property(e => e.Processed).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.ProcessedAt).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.LastError).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.NextRetryAt).IsModified = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// GAP-R6-RT-05 Fix: re-attach + mark Modified trên các property cho phép (Processed/RetryCount/LastError/NextRetryAt).
        /// Dùng sau TryClaimEventAsync khi caller muốn ghi metadata mà không qua MarkProcessed/MarkFailed.
        /// </summary>
        public Task UpdateAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
        {
            _db.OutboxEvents.Attach(outboxEvent);
            _db.Entry(outboxEvent).Property(e => e.RetryCount).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.LastError).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.NextRetryAt).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.Processed).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.ProcessedAt).IsModified = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// GAP-R4-A8 Fix: Exponential backoff khi publish fail.
        /// Backoff = 10s × 2^(retry-1), capped 300s (5 phút).
        /// Tránh poison-pill scenario: 1 event fail liên tục sẽ chiếm mỗi tick 5s
        /// để hammer external API. Với backoff, sẽ retry ở tick 10s, 30s, 60s, 120s, 240s.
        /// </summary>
        public Task MarkFailedAsync(OutboxEvent outboxEvent, string errorMessage, CancellationToken cancellationToken = default)
        {
            _db.OutboxEvents.Attach(outboxEvent);
            outboxEvent.RetryCount += 1;
            outboxEvent.LastError = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
            // Exponential backoff: 10s, 20s, 40s, 80s, 160s, 300s (cap)
            var backoffSeconds = Math.Min(300, 10 * (int)Math.Pow(2, Math.Min(outboxEvent.RetryCount - 1, 5)));
            outboxEvent.NextRetryAt = DateTime.UtcNow.AddSeconds(backoffSeconds);
            _db.Entry(outboxEvent).Property(e => e.RetryCount).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.LastError).IsModified = true;
            _db.Entry(outboxEvent).Property(e => e.NextRetryAt).IsModified = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// GAP-R4-A8 Fix: Cleanup processed events cũ (>30 ngày) — tránh bảng OutboxEvents
        /// grow indefinitely. Với 1000 lobby/day × 5 events = 900k rows/6 tháng.
        /// Production cần schedule OutboxCleanupJob daily.
        /// </summary>
        public async Task<int> DeleteProcessedOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
        {
            return await _db.OutboxEvents
                .Where(e => e.Processed && e.ProcessedAt != null && e.ProcessedAt < cutoff)
                .ExecuteDeleteAsync(ct);
        }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}