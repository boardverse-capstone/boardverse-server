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

    public Task<SeatInventory?> GetAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime)
    {
        return _db.SeatInventories
            .FirstOrDefaultAsync(s => s.CafeId == cafeId
                && s.PlayDate == playDate
                && s.ScheduledStartTime == scheduledStartTime
                && s.ScheduledEndTime == scheduledEndTime);
    }

    public Task<SeatInventory?> GetForUpdateAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime)
    {
        return _db.SeatInventories.FromSqlRaw(
            @"SELECT * FROM ""SeatInventories""
              WHERE ""CafeId"" = {0} AND ""PlayDate"" = {1} AND ""ScheduledStartTime"" = {2} AND ""ScheduledEndTime"" = {3}
              FOR UPDATE",
            cafeId, playDate, scheduledStartTime, scheduledEndTime)
            .FirstOrDefaultAsync();
    }

    public Task<SeatInventory?> GetByIdForUpdateAsync(Guid id)
    {
        return _db.SeatInventories.FromSqlRaw(
            @"SELECT * FROM ""SeatInventories"" WHERE ""Id"" = {0} FOR UPDATE",
            id)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<SeatInventory>> GetByCafeAsync(Guid cafeId, DateOnly fromDate, DateOnly toDate)
    {
        return await _db.SeatInventories
            .Where(s => s.CafeId == cafeId && s.PlayDate >= fromDate && s.PlayDate <= toDate)
            .ToListAsync();
    }

    public async Task EnsureRowAsync(Guid cafeId, DateOnly playDate, TimeOnly scheduledStartTime, TimeOnly scheduledEndTime, int totalSeats)
    {
        var existing = await GetAsync(cafeId, playDate, scheduledStartTime, scheduledEndTime);
        if (existing == null)
        {
            existing = new SeatInventory
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                PlayDate = playDate,
                ScheduledStartTime = scheduledStartTime,
                ScheduledEndTime = scheduledEndTime,
                TotalSeats = totalSeats,
                HeldSeats = 0,
                InUseSeats = 0,
                RowVersion = 0
            };
            await AddAsync(existing);
            await SaveChangesAsync();
        }
    }

    public Task AddAsync(SeatInventory seatInventory)
    {
        _db.SeatInventories.Add(seatInventory);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SeatInventory seatInventory)
    {
        seatInventory.UpdatedAt = DateTime.UtcNow;
        seatInventory.RowVersion++;
        _db.SeatInventories.Update(seatInventory);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}
