using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class DeviceTokenRepository : IDeviceTokenRepository
{
    private readonly BoardVerseDbContext _db;

    public DeviceTokenRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(DeviceToken token)
    {
        await _db.DeviceTokens.AddAsync(token);
    }

    public async Task<DeviceToken?> GetByTokenAsync(string token)
    {
        return await _db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token);
    }

    public async Task<IReadOnlyList<DeviceToken>> GetByUserIdAsync(Guid userId)
    {
        return await _db.DeviceTokens
            .Where(t => t.UserId == userId && !t.IsInvalidated)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DeviceToken>> GetActiveTokensByUserIdsAsync(IReadOnlyCollection<Guid> userIds)
    {
        if (userIds.Count == 0)
        {
            return Array.Empty<DeviceToken>();
        }
        return await _db.DeviceTokens
            .Where(t => userIds.Contains(t.UserId) && !t.IsInvalidated)
            .ToListAsync();
    }

    public async Task UpdateAsync(DeviceToken token)
    {
        _db.DeviceTokens.Update(token);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _db.DeviceTokens.FindAsync(id);
        if (entity != null)
        {
            _db.DeviceTokens.Remove(entity);
        }
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
