using BoardVerse.Core.DTOs.Rating;

namespace BoardVerse.Services.IServices
{
    public interface IKarmaRatingService
    {
        Task<LobbyKarmaRatingContextDto> GetLobbyRatingContextAsync(Guid raterUserId, Guid lobbyId);
        Task<SubmitKarmaRatingsResponseDto> SubmitKarmaRatingsAsync(
            Guid raterUserId,
            SubmitKarmaRatingsRequestDto request);
        Task<LobbyKarmaRatingNotificationDto> OpenLobbyKarmaRatingWindowAsync(Guid lobbyId);
    }
}
