using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IMatchResultRepository
    {
        Task<Lobby?> GetLobbyForMatchAsync(Guid lobbyId);
        Task<bool> IsActiveLobbyMemberAsync(Guid lobbyId, Guid userId);
        Task<bool> GameSupportsMatchResultsAsync(Guid gameTemplateId);
        Task<MatchResult?> GetSubmissionAsync(Guid lobbyId, Guid userId);
        Task<IReadOnlyList<MatchResult>> GetSubmissionsAsync(Guid lobbyId);
        Task<MatchHistory?> GetFinalizedHistoryAsync(Guid lobbyId);
        Task AddSubmissionAsync(MatchResult submission);
        Task AddMatchHistoryAsync(MatchHistory history);
        Task<UserProfile?> GetProfileForUpdateAsync(Guid userId);
        Task SaveChangesAsync();
    }
}
