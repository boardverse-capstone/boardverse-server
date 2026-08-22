using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IKarmaRatingRepository
    {
        Task<Lobby?> GetLobbyForRatingAsync(Guid lobbyId, CancellationToken cancellationToken = default);
        Task<Lobby?> GetLobbyForUpdateAsync(Guid lobbyId, CancellationToken cancellationToken = default);
        Task<bool> IsActiveLobbyMemberAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default);
        Task<bool> HasRatingAsync(Guid lobbyId, Guid raterUserId, Guid targetUserId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Guid>> GetRatedTargetIdsAsync(Guid lobbyId, Guid raterUserId, CancellationToken cancellationToken = default);
        Task AddRatingAsync(PlayerKarmaRating rating, CancellationToken cancellationToken = default);
        Task AddKarmaLogAsync(KarmaLog log, CancellationToken cancellationToken = default);
        Task<UserProfile?> GetProfileForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
