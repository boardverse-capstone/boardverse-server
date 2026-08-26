using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface ILobbyMemberRepository
{
    Task<LobbyMember?> GetByLobbyAndUserAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LobbyMember>> GetByLobbyAsync(Guid lobbyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LobbyMember>> GetActiveByLobbyAsync(Guid lobbyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách UserId đã chơi chung lobby với userId trong N ngày gần đây.
    /// Dùng cho friend suggestion.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetRecentMemberUserIdsAsync(Guid userId, int daysBack = 30, int maxLobbies = 50, CancellationToken cancellationToken = default);
}
