using BoardVerse.Core.DTOs.Tournament;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface ITournamentSpectatorService
{
    Task<TournamentSpectatorDto> SpectateAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task LeaveSpectateAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentSpectatorDto?> GetMySpectatorEntryAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentSpectatorDto>> GetSpectatorsAsync(Guid tournamentId, CancellationToken cancellationToken = default);
}
