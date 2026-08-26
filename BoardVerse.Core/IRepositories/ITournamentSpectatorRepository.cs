using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface ITournamentSpectatorRepository
{
    Task<TournamentSpectator?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TournamentSpectator?> GetByUserAsync(Guid tournamentId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentSpectator>> GetByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task AddAsync(TournamentSpectator spectator, CancellationToken cancellationToken = default);
    Task UpdateAsync(TournamentSpectator spectator, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
