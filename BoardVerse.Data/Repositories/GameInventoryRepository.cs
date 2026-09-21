using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class GameInventoryRepository : IGameInventoryRepository
{
    private readonly BoardVerseDbContext _db;

    public GameInventoryRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<GameInventory?> GetAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default)
    {
        return _db.GameInventories
            .FirstOrDefaultAsync(g => g.CafeId == cafeId
                && g.GameId == gameId
                && g.PlayDate == playDate
                && g.ScheduledStartTime == scheduledStartTime
                && g.ScheduledEndTime == scheduledEndTime);
    }

    public Task<GameInventory?> GetForUpdateAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default)
    {
        // FIX 2026-09-21: AsNoTracking() prevents EF Core from wrapping FromSqlRaw in a subquery.
        // Without AsNoTracking(), EF Core wraps the raw SQL in a subquery that does NOT include xmin
        // in the inner SELECT (shadow property), causing "column b.xmin does not exist" at the outer
        // SELECT. With AsNoTracking() the raw SQL runs directly and xmin is included in the result
        // set — the entity is still materialised with the xmin shadow property for the concurrency
        // token in SaveChangesAsync.
        return _db.GameInventories.FromSqlRaw(
            @"SELECT ""Id"", ""CafeId"", ""GameId"", ""PlayDate"", ""ScheduledStartTime"", ""ScheduledEndTime"",
                     ""TotalCopies"", ""HeldCopies"", ""InUseCopies"", ""CreatedAt"", ""UpdatedAt"", xmin
              FROM ""GameInventories""
              WHERE ""CafeId"" = {0} AND ""GameId"" = {1} AND ""PlayDate"" = {2} AND ""ScheduledStartTime"" = {3} AND ""ScheduledEndTime"" = {4}
              FOR UPDATE",
            cafeId, gameId, playDate, scheduledStartTime, scheduledEndTime)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<GameInventory?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Same fix as GetForUpdateAsync — AsNoTracking() prevents EF Core subquery wrapping
        // that silently drops xmin from the SELECT, which would cause the concurrency UPDATE
        // (WHERE xmin = @original) to use a NULL token and always fail.
        return _db.GameInventories.FromSqlRaw(
            @"SELECT ""Id"", ""CafeId"", ""GameId"", ""PlayDate"", ""ScheduledStartTime"", ""ScheduledEndTime"",
                     ""TotalCopies"", ""HeldCopies"", ""InUseCopies"", ""CreatedAt"", ""UpdatedAt"", xmin
              FROM ""GameInventories"" WHERE ""Id"" = {0} FOR UPDATE",
            id)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task EnsureRowAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, int totalCopies, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(cafeId, gameId, playDate, scheduledStartTime, scheduledEndTime);
        if (existing == null)
        {
            existing = new GameInventory
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                GameId = gameId,
                PlayDate = playDate,
                ScheduledStartTime = scheduledStartTime,
                ScheduledEndTime = scheduledEndTime,
                TotalCopies = totalCopies,
                HeldCopies = 0,
                InUseCopies = 0
                // xmin (concurrency token) is PostgreSQL system column — auto-managed, no manual init needed
            };
            _db.GameInventories.Add(existing);
            await SaveChangesAsync();
        }
    }

    public Task UpdateAsync(GameInventory gameInventory, CancellationToken cancellationToken = default)
    {
        gameInventory.UpdatedAt = DateTime.UtcNow;
        // xmin (concurrency token) is PostgreSQL system column — auto-managed by DB, no manual increment needed
        _db.GameInventories.Update(gameInventory);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync();
    }
}
