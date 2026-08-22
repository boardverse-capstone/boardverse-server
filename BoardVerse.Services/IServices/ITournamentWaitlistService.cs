using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface ITournamentWaitlistService
{
    Task<TournamentWaitlistEntryDto> JoinWaitlistAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentWaitlistEntryDto>> GetWaitlistAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentWaitlistEntryDto?> GetMyWaitlistEntryAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task CancelWaitlistAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentWaitlistEntryDto> ConfirmFromWaitlistAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentWaitlistEntryDto> DeclineOfferAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
}
