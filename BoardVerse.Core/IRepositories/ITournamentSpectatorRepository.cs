using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface ITournamentSpectatorRepository
{
    Task<TournamentSpectator?> GetByIdAsync(Guid id);
    Task<TournamentSpectator?> GetByUserAsync(Guid tournamentId, Guid userId);
    Task<IReadOnlyList<TournamentSpectator>> GetByTournamentAsync(Guid tournamentId);
    Task AddAsync(TournamentSpectator spectator);
    Task UpdateAsync(TournamentSpectator spectator);
    Task DeleteAsync(Guid id);
    Task SaveChangesAsync();
}
