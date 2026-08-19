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

    public Task AddAsync(OutboxEvent outboxEvent)
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
    /// </summary>
    public async Task<IReadOnlyList<OutboxEvent>> FetchUnprocessedBatchAsync(int batchSize)
    {
        if (batchSize <= 0)
        {
            batchSize = 50;
        }

        // Lock rows không có ai đang giữ → cluster-safe.
        // batchSize đã validate (1..50), nối literal trực tiếp, không SQL injection risk.
        var sql = $"SELECT * FROM \"OutboxEvents\" WHERE \"Processed\" = false " +
                  $"ORDER BY \"CreatedAt\" ASC LIMIT {batchSize} FOR UPDATE SKIP LOCKED";

        return await _db.OutboxEvents
            .FromSqlRaw(sql)
            .AsNoTracking()
            .ToListAsync();
    }

    public Task MarkProcessedAsync(OutboxEvent outboxEvent)
    {
        // Re-attach (vì đã AsNoTracking ở fetch) rồi update.
        _db.OutboxEvents.Attach(outboxEvent);
        outboxEvent.Processed = true;
        outboxEvent.ProcessedAt = DateTime.UtcNow;
        outboxEvent.LastError = null;
        _db.Entry(outboxEvent).Property(e => e.Processed).IsModified = true;
        _db.Entry(outboxEvent).Property(e => e.ProcessedAt).IsModified = true;
        _db.Entry(outboxEvent).Property(e => e.LastError).IsModified = true;
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(OutboxEvent outboxEvent, string errorMessage)
    {
        _db.OutboxEvents.Attach(outboxEvent);
        outboxEvent.RetryCount += 1;
        outboxEvent.LastError = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        _db.Entry(outboxEvent).Property(e => e.RetryCount).IsModified = true;
        _db.Entry(outboxEvent).Property(e => e.LastError).IsModified = true;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}