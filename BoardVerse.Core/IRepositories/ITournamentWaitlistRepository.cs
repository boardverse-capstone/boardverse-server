using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

public interface ITournamentWaitlistRepository
{
    Task<TournamentWaitlist?> GetByIdAsync(Guid id);
    Task<TournamentWaitlist?> GetPendingByUserAsync(Guid tournamentId, Guid userId);
    Task<IReadOnlyList<TournamentWaitlist>> GetByTournamentAsync(Guid tournamentId, TournamentWaitlistStatus? status = null);
    Task<int> GetNextPositionAsync(Guid tournamentId);
    Task AddAsync(TournamentWaitlist entry);
    Task UpdateAsync(TournamentWaitlist entry);
    Task DeleteAsync(Guid id);
    Task SaveChangesAsync();
}
