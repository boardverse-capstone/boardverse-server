using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class FriendshipRepository : IFriendshipRepository
{
    private readonly BoardVerseDbContext _db;

    public FriendshipRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<Friendship?> GetByIdAsync(Guid id)
    {
        return await _db.Friendships
            .Include(f => f.Requester).ThenInclude(u => u.Profile)
            .Include(f => f.Addressee).ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<Friendship?> GetByPairAsync(Guid userAId, Guid userBId)
    {
        return await _db.Friendships
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == userAId && f.AddresseeId == userBId) ||
                (f.RequesterId == userBId && f.AddresseeId == userAId));
    }

    public async Task<IReadOnlyList<Friendship>> GetByUserAsync(Guid userId, FriendshipStatus? status = null)
    {
        var query = _db.Friendships
            .Include(f => f.Requester).ThenInclude(u => u.Profile)
            .Include(f => f.Addressee).ThenInclude(u => u.Profile)
            .Where(f => f.RequesterId == userId || f.AddresseeId == userId);

        if (status.HasValue)
        {
            query = query.Where(f => f.Status == status.Value);
        }

        return await query.OrderByDescending(f => f.UpdatedAt).ToListAsync();
    }

    public async Task<IReadOnlyList<Friendship>> GetFriendsAsync(Guid userId)
    {
        return await _db.Friendships
            .Include(f => f.Requester).ThenInclude(u => u.Profile)
            .Include(f => f.Addressee).ThenInclude(u => u.Profile)
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                        (f.RequesterId == userId || f.AddresseeId == userId))
            .OrderByDescending(f => f.AcceptedAt ?? f.UpdatedAt)
            .ToListAsync();
    }

    public async Task<int> CountFriendsAsync(Guid userId)
    {
        return await _db.Friendships
            .CountAsync(f => f.Status == FriendshipStatus.Accepted &&
                             (f.RequesterId == userId || f.AddresseeId == userId));
    }

    public async Task<IReadOnlyList<Guid>> GetFriendUserIdsAsync(Guid userId)
    {
        var friends = await _db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                        (f.RequesterId == userId || f.AddresseeId == userId))
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync();
        return friends;
    }

    public async Task<int> CountMutualFriendsAsync(Guid userAId, Guid userBId)
    {
        var aFriends = (await GetFriendUserIdsAsync(userAId)).ToHashSet();
        if (aFriends.Count == 0) return 0;
        var bFriends = await GetFriendUserIdsAsync(userBId);
        return bFriends.Count(aFriends.Contains);
    }

    public async Task<IReadOnlyList<Guid>> GetMutualFriendIdsAsync(Guid currentUserId, Guid otherUserId)
    {
        var currentFriends = (await GetFriendUserIdsAsync(currentUserId)).ToHashSet();
        if (currentFriends.Count == 0) return Array.Empty<Guid>();
        var otherFriends = await GetFriendUserIdsAsync(otherUserId);
        return otherFriends.Where(currentFriends.Contains).ToList();
    }

    public async Task<IReadOnlyList<Friendship>> GetExpiredPendingAsync(DateTime cutoff)
    {
        return await _db.Friendships
            .Where(f => f.Status == FriendshipStatus.Pending && f.CreatedAt <= cutoff)
            .ToListAsync();
    }

    public Task AddAsync(Friendship friendship)
    {
        _db.Friendships.Add(friendship);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}
