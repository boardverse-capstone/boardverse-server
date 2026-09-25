using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class SeatInventoryRepository : ISeatInventoryRepository
{
    private readonly BoardVerseDbContext _db;

    public SeatInventoryRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<SeatInventory?> GetAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default)
    {
        return _db.SeatInventories
            .FirstOrDefaultAsync(s => s.CafeId == cafeId
                && s.PlayDate == playDate
                && s.ScheduledStartTime == scheduledStartTime
                && s.ScheduledEndTime == scheduledEndTime);
    }

    public Task<SeatInventory?> GetForUpdateAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, CancellationToken cancellationToken = default)
    {
        // FIX 2026-09-21: AsNoTracking() prevents EF Core from wrapping FromSqlRaw in a subquery.
        // Without AsNoTracking(), EF Core wraps the raw SQL in a subquery that does NOT include xmin
        // in the inner SELECT (shadow property), causing "column b.xmin does not exist" at the outer
        // SELECT. With AsNoTracking() the raw SQL runs directly and xmin is included in the result
        // set — the entity is still materialised with the xmin shadow property for the concurrency
        // token in SaveChangesAsync.
        return _db.SeatInventories.FromSqlRaw(
            @"SELECT ""Id"", ""CafeId"", ""PlayDate"", ""ScheduledStartTime"", ""ScheduledEndTime"",
                     ""TotalSeats"", ""HeldSeats"", ""InUseSeats"", ""CreatedAt"", ""UpdatedAt"", xmin
              FROM ""SeatInventories""
              WHERE ""CafeId"" = {0} AND ""PlayDate"" = {1} AND ""ScheduledStartTime"" = {2} AND ""ScheduledEndTime"" = {3}
              FOR UPDATE",
            cafeId, playDate, scheduledStartTime, scheduledEndTime)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<SeatInventory?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Same fix as GetForUpdateAsync — AsNoTracking() prevents EF Core subquery wrapping
        // that silently drops xmin from the SELECT, which would cause the concurrency UPDATE
        // (WHERE xmin = @original) to use a NULL token and always fail.
        return _db.SeatInventories.FromSqlRaw(
            @"SELECT ""Id"", ""CafeId"", ""PlayDate"", ""ScheduledStartTime"", ""ScheduledEndTime"",
                     ""TotalSeats"", ""HeldSeats"", ""InUseSeats"", ""CreatedAt"", ""UpdatedAt"", xmin
              FROM ""SeatInventories"" WHERE ""Id"" = {0} FOR UPDATE",
            id)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SeatInventory>> GetByCafeAsync(Guid cafeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        return await _db.SeatInventories
            .Where(s => s.CafeId == cafeId && s.PlayDate >= fromDate && s.PlayDate <= toDate)
            .ToListAsync();
    }

    public async Task EnsureRowAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, int totalSeats, CancellationToken cancellationToken = default)
    {
        // FIX 2026-09-25: chống TOCTOU race vs. concurrent EnsureRowAsync callers.
        // Bug cũ: SELECT (GetAsync) → null → INSERT (SaveChangesAsync). Hai request
        // cùng (CafeId, PlayDate, ScheduledStartTime, ScheduledEndTime) chạy song song
        // đều lọt qua SELECT, đều INSERT, cái sau nổ PostgresException 23505
        // vi phạm UX_SeatInventories_Cafe_PlayDate_Times. Postgres xử lý conflict ở
        // DB level qua ON CONFLICT, nên an toàn dù 100 request đụng cùng khung giờ.
        var now = DateTime.UtcNow;
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""SeatInventories""
                (""Id"", ""CafeId"", ""PlayDate"", ""ScheduledStartTime"", ""ScheduledEndTime"",
                 ""TotalSeats"", ""HeldSeats"", ""InUseSeats"", ""CreatedAt"", ""UpdatedAt"")
            VALUES
                ({Guid.NewGuid()}, {cafeId}, {playDate}, {scheduledStartTime}, {scheduledEndTime},
                 {totalSeats}, 0, 0, {now}, {now})
            ON CONFLICT (""CafeId"", ""PlayDate"", ""ScheduledStartTime"", ""ScheduledEndTime"") DO NOTHING;
        ", cancellationToken);

        // Caller không dùng giá trị trả về; transaction tiếp theo sẽ load row qua
        // GetForUpdateAsync (SELECT ... FOR UPDATE) để đảm bảo thấy row dù bất kỳ
        // request nào (kể cả request khác vừa insert) đã tạo.
    }

    public Task AddAsync(SeatInventory seatInventory, CancellationToken cancellationToken = default)
    {
        _db.SeatInventories.Add(seatInventory);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SeatInventory seatInventory, CancellationToken cancellationToken = default)
    {
        seatInventory.UpdatedAt = DateTime.UtcNow;
        // xmin (concurrency token) is PostgreSQL system column — auto-managed by DB, no manual increment needed
        _db.SeatInventories.Update(seatInventory);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync();
    }
}
