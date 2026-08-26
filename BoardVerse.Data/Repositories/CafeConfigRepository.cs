using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class CafeConfigRepository : ICafeConfigRepository
{
    private readonly BoardVerseDbContext _db;

    public CafeConfigRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<CafeConfig?> GetByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        return _db.CafeConfigs.FirstOrDefaultAsync(c => c.CafeId == cafeId);
    }

    public async Task<CafeConfig> GetOrCreateDefaultAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        var existing = await GetByCafeIdAsync(cafeId);
        if (existing != null) return existing;

        existing = new CafeConfig
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId
            // Defaults từ configuration: BR-NEW-01 §8, BR-NEW-12 §XIII.
        };
        _db.CafeConfigs.Add(existing);
        await SaveChangesAsync();
        return existing;
    }

    public Task UpdateAsync(CafeConfig config, CancellationToken cancellationToken = default)
    {
        config.UpdatedAt = DateTime.UtcNow;
        _db.CafeConfigs.Update(config);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync();
    }
}