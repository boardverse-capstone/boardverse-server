using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface ITournamentWaitlistRepository
{
    Task<TournamentWaitlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TournamentWaitlist?> GetPendingByUserAsync(Guid tournamentId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentWaitlist>> GetByTournamentAsync(Guid tournamentId, TournamentWaitlistStatus? status = null, CancellationToken cancellationToken = default);
    Task<int> GetNextPositionAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task AddAsync(TournamentWaitlist entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(TournamentWaitlist entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
