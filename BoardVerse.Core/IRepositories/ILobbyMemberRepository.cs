using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface ILobbyMemberRepository
{
    Task<LobbyMember?> GetByLobbyAndUserAsync(Guid lobbyId, Guid userId);
    Task<IReadOnlyList<LobbyMember>> GetByLobbyAsync(Guid lobbyId);
    Task<IReadOnlyList<LobbyMember>> GetActiveByLobbyAsync(Guid lobbyId);

    /// <summary>
    /// Lấy danh sách UserId đã chơi chung lobby với userId trong N ngày gần đây.
    /// Dùng cho friend suggestion.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetRecentMemberUserIdsAsync(Guid userId, int daysBack = 30, int maxLobbies = 50);
}
