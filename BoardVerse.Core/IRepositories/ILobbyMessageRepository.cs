using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ILobbyMessageRepository
    {
        Task<LobbyMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<LobbyMessage>> GetByLobbyAsync(Guid lobbyId, DateTime? beforeCursor, int limit = 50, CancellationToken cancellationToken = default);
        Task AddAsync(LobbyMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Hard-delete toàn bộ messages của một lobby. Dùng khi dissolve lobby.
        /// </summary>
        Task RemoveByLobbyAsync(Guid lobbyId, CancellationToken cancellationToken = default);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}