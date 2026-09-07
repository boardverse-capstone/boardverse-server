using BoardVerse.Core.DTOs.Discovery;

namespace BoardVerse.Services.IServices;

public interface IBoardGameDiscoveryService
{
    Task<BoardGameSurveyResponseDto> RunSurveyAsync(
        BoardGameSurveyRequestDto request,
        Guid? userId,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken = default);

    Task<List<SavedBoardGameDto>> GetSavedGamesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<BoardGameSaveResultDto> ToggleSaveAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default);
}
