using BoardVerse.Core.DTOs.Match;

namespace BoardVerse.Services.IServices
{
    public interface IMatchResultService
    {
        Task<MatchResultStatusDto> GetMatchResultStatusAsync(Guid userId, Guid lobbyId);
        Task<SubmitMatchResultResponseDto> SubmitMatchResultAsync(
            Guid userId,
            SubmitMatchResultRequestDto request);
    }
}
