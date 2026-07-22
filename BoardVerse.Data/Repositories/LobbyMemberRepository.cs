using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class LobbyMemberRepository : ILobbyMemberRepository
{
    private readonly BoardVerseDbContext _db;

    public LobbyMemberRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<LobbyMember?> GetByLobbyAndUserAsync(Guid lobbyId, Guid userId)
        => _db.LobbyMembers.FirstOrDefaultAsync(m => m.LobbyId == lobbyId && m.UserId == userId);

    public async Task<IReadOnlyList<LobbyMember>> GetByLobbyAsync(Guid lobbyId)
        => await _db.LobbyMembers.Where(m => m.LobbyId == lobbyId).ToListAsync();

    public async Task<IReadOnlyList<LobbyMember>> GetActiveByLobbyAsync(Guid lobbyId)
        => await _db.LobbyMembers.Where(m => m.LobbyId == lobbyId && m.IsActive).ToListAsync();

    public async Task<IReadOnlyList<Guid>> GetRecentMemberUserIdsAsync(Guid userId, int daysBack = 30, int maxLobbies = 50)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysBack);

        // Lấy danh sách lobby user đã tham gia gần đây
        var myLobbyIds = await _db.LobbyMembers
            .Where(m => m.UserId == userId && m.JoinedAt >= cutoff)
            .OrderByDescending(m => m.JoinedAt)
            .Take(maxLobbies)
            .Select(m => m.LobbyId)
            .ToListAsync();

        if (myLobbyIds.Count == 0) return Array.Empty<Guid>();

        // Lấy các user khác trong cùng lobby
        var otherUserIds = await _db.LobbyMembers
            .Where(m => myLobbyIds.Contains(m.LobbyId)
                        && m.UserId != userId
                        && m.IsActive)
            .Select(m => m.UserId)
            .Distinct()
            .Take(100)
            .ToListAsync();

        return otherUserIds;
    }
}
