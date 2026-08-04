using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ILobbyMessageRepository
    {
        Task<LobbyMessage?> GetByIdAsync(Guid id);
        Task<IReadOnlyList<LobbyMessage>> GetByLobbyAsync(Guid lobbyId, DateTime? beforeCursor, int limit = 50);
        Task AddAsync(LobbyMessage message);

        /// <summary>
        /// Hard-delete toàn bộ messages của một lobby. Dùng khi dissolve lobby.
        /// </summary>
        Task RemoveByLobbyAsync(Guid lobbyId);

        Task SaveChangesAsync();
    }
}