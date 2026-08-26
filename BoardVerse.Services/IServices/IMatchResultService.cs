using BoardVerse.Core.DTOs.Match;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IMatchResultService
    {
        Task<MatchResultStatusDto> GetMatchResultStatusAsync(Guid userId, Guid lobbyId, CancellationToken cancellationToken = default);
        Task<SubmitMatchResultResponseDto> SubmitMatchResultAsync(
            Guid userId,
            SubmitMatchResultRequestDto request, CancellationToken cancellationToken = default);
    }
}
