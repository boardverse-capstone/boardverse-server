using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IMatchResultRepository
    {
        Task<Lobby?> GetLobbyForMatchAsync(Guid lobbyId, CancellationToken cancellationToken = default);
        Task<bool> IsActiveLobbyMemberAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default);
        Task<bool> GameSupportsMatchResultsAsync(Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<MatchResult?> GetSubmissionAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MatchResult>> GetSubmissionsAsync(Guid lobbyId, CancellationToken cancellationToken = default);
        Task<MatchHistory?> GetFinalizedHistoryAsync(Guid lobbyId, CancellationToken cancellationToken = default);
        Task AddSubmissionAsync(MatchResult submission, CancellationToken cancellationToken = default);
        Task AddMatchHistoryAsync(MatchHistory history, CancellationToken cancellationToken = default);
        Task<UserProfile?> GetProfileForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
