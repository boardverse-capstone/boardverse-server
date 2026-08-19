using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
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

    public Task<GameInventory?> GetAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeSlot timeSlot)
    {
        return _db.GameInventories
            .FirstOrDefaultAsync(g => g.CafeId == cafeId
                && g.GameId == gameId
                && g.PlayDate == playDate
                && g.TimeSlot == timeSlot);
    }

    public Task<GameInventory?> GetForUpdateAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeSlot timeSlot)
    {
        return _db.GameInventories.FromSqlRaw(
            @"SELECT * FROM ""GameInventories""
              WHERE ""CafeId"" = {0} AND ""GameId"" = {1} AND ""PlayDate"" = {2} AND ""TimeSlot"" = {3}
              FOR UPDATE",
            cafeId, gameId, playDate, (int)timeSlot)
            .FirstOrDefaultAsync();
    }

    public async Task EnsureRowAsync(Guid cafeId, Guid gameId, DateOnly playDate, TimeSlot timeSlot, int totalCopies)
    {
        var existing = await GetAsync(cafeId, gameId, playDate, timeSlot);
        if (existing == null)
        {
            existing = new GameInventory
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                GameId = gameId,
                PlayDate = playDate,
                TimeSlot = timeSlot,
                TotalCopies = totalCopies,
                HeldCopies = 0,
                InUseCopies = 0,
                RowVersion = 0
            };
            _db.GameInventories.Add(existing);
            await SaveChangesAsync();
        }
    }

    public Task UpdateAsync(GameInventory gameInventory)
    {
        gameInventory.UpdatedAt = DateTime.UtcNow;
        gameInventory.RowVersion++;
        _db.GameInventories.Update(gameInventory);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}