using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

public interface ITournamentWaitlistService
{
    Task<TournamentWaitlistEntryDto> JoinWaitlistAsync(Guid userId, Guid tournamentId);
    Task<IReadOnlyList<TournamentWaitlistEntryDto>> GetWaitlistAsync(Guid tournamentId);
    Task<TournamentWaitlistEntryDto?> GetMyWaitlistEntryAsync(Guid userId, Guid tournamentId);
    Task CancelWaitlistAsync(Guid userId, Guid tournamentId);
    Task<TournamentWaitlistEntryDto> ConfirmFromWaitlistAsync(Guid userId, Guid tournamentId);
    Task<TournamentWaitlistEntryDto> DeclineOfferAsync(Guid userId, Guid tournamentId);
}
