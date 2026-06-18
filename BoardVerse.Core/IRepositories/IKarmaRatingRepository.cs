using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IKarmaRatingRepository
    {
        Task<Lobby?> GetLobbyForRatingAsync(Guid lobbyId);
        Task<Lobby?> GetLobbyForUpdateAsync(Guid lobbyId);
        Task<bool> IsActiveLobbyMemberAsync(Guid lobbyId, Guid userId);
        Task<bool> HasRatingAsync(Guid lobbyId, Guid raterUserId, Guid targetUserId);
        Task<IReadOnlyList<Guid>> GetRatedTargetIdsAsync(Guid lobbyId, Guid raterUserId);
        Task AddRatingAsync(PlayerKarmaRating rating);
        Task AddKarmaLogAsync(KarmaLog log);
        Task<UserProfile?> GetProfileForUpdateAsync(Guid userId);
        Task SaveChangesAsync();
    }
}
