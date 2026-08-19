using BoardVerse.Core.DTOs.Tournament;

namespace BoardVerse.Services.IServices;

public interface ITournamentSpectatorService
{
    Task<TournamentSpectatorDto> SpectateAsync(Guid userId, Guid tournamentId);
    Task LeaveSpectateAsync(Guid userId, Guid tournamentId);
    Task<TournamentSpectatorDto?> GetMySpectatorEntryAsync(Guid userId, Guid tournamentId);
    Task<IReadOnlyList<TournamentSpectatorDto>> GetSpectatorsAsync(Guid tournamentId);
}
