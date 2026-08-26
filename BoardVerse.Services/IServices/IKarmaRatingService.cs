using BoardVerse.Core.DTOs.Rating;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IKarmaRatingService
    {
        Task<LobbyKarmaRatingContextDto> GetLobbyRatingContextAsync(Guid raterUserId, Guid lobbyId, CancellationToken cancellationToken = default);
        Task<SubmitKarmaRatingsResponseDto> SubmitKarmaRatingsAsync(
            Guid raterUserId,
            SubmitKarmaRatingsRequestDto request, CancellationToken cancellationToken = default);
        Task<LobbyKarmaRatingNotificationDto> OpenLobbyKarmaRatingWindowAsync(Guid lobbyId);
    }
}
