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

    /// <summary>
    /// Xóa một board game khỏi danh sách đã lưu của player. Nếu game chưa được lưu → ném 404.
    /// </summary>
    /// <param name="userId">Player xóa lưu.</param>
    /// <param name="gameTemplateId">Game cần xóa khỏi danh sách đã lưu.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BoardGameNotFoundException">Game không tồn tại.</exception>
    /// <exception cref="NotFoundException">Game chưa nằm trong danh sách đã lưu của player.</exception>
    Task<BoardGameSaveResultDto> UnsaveGameAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default);
}
