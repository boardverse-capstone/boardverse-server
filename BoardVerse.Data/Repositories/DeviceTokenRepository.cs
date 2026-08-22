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

    public async Task AddAsync(DeviceToken token, CancellationToken cancellationToken = default)
    {
        await _db.DeviceTokens.AddAsync(token);
    }

    public async Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token);
    }

    public async Task<IReadOnlyList<DeviceToken>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.DeviceTokens
            .Where(t => t.UserId == userId && !t.IsInvalidated)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DeviceToken>> GetActiveTokensByUserIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return Array.Empty<DeviceToken>();
        }
        return await _db.DeviceTokens
            .Where(t => userIds.Contains(t.UserId) && !t.IsInvalidated)
            .ToListAsync();
    }

    public async Task UpdateAsync(DeviceToken token, CancellationToken cancellationToken = default)
    {
        _db.DeviceTokens.Update(token);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.DeviceTokens.FindAsync(id);
        if (entity != null)
        {
            _db.DeviceTokens.Remove(entity);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// GAP-R6-FCM-CLEANUP Fix: hard-delete stale tokens.
    /// Xóa: (IsInvalidated=true) OR (LastUsedAt &lt; staleCutoff).
    /// Dùng ExecuteDeleteAsync cho batch DELETE thay vì load + remove entity (memory efficient).
    /// </summary>
    public async Task<int> DeleteStaleTokensAsync(DateTime staleCutoff, CancellationToken cancellationToken = default)
    {
        return await _db.DeviceTokens
            .Where(t => t.IsInvalidated || t.LastSeenAt < staleCutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
