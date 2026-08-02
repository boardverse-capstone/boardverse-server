using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
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

    public Task<SeatInventory?> GetAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot)
    {
        return _db.SeatInventories
            .FirstOrDefaultAsync(s => s.CafeId == cafeId
                && s.PlayDate == playDate
                && s.TimeSlot == timeSlot);
    }

    public Task<SeatInventory?> GetForUpdateAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot)
    {
        return _db.SeatInventories.FromSqlRaw(
            @"SELECT * FROM ""SeatInventories""
              WHERE ""CafeId"" = {0} AND ""PlayDate"" = {1} AND ""TimeSlot"" = {2}
              FOR UPDATE",
            cafeId, playDate, (int)timeSlot)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<SeatInventory>> GetByCafeAsync(Guid cafeId, DateOnly fromDate, DateOnly toDate)
    {
        return await _db.SeatInventories
            .Where(s => s.CafeId == cafeId && s.PlayDate >= fromDate && s.PlayDate <= toDate)
            .ToListAsync();
    }

    public async Task EnsureRowAsync(Guid cafeId, DateOnly playDate, TimeSlot timeSlot, int totalSeats)
    {
        var existing = await GetAsync(cafeId, playDate, timeSlot);
        if (existing == null)
        {
            existing = new SeatInventory
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                PlayDate = playDate,
                TimeSlot = timeSlot,
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