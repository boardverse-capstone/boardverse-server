using BoardVerse.Core.DTOs.Discovery;

namespace BoardVerse.Services.IServices;

public interface IBoardGameDiscoveryService
{
    /// <summary>
    /// Khảo sát gợi ý board game cho một player.
    /// </summary>
    Task<BoardGameSurveyResponseDto> RunSurveyAsync(
        BoardGameSurveyRequestDto request,
        Guid? userId,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gợi ý board game cho nhóm nhiều người với sở thích khác nhau (AWM algorithm).
    /// Mỗi sub-group có thể có ExperienceLevel, CategoryIds, PreferredDurations riêng.
    /// </summary>
    Task<GroupDiscoveryResponseDto> GroupDiscoveryAsync(
        GroupDiscoveryRequestDto request,
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

    /// <summary>
    /// Gợi ý board game solo dựa trên preference profile của user (từ saved games + play history).
    /// Yêu cầu user phải có ít nhất 3 saved games. Nếu không đủ → trả về profile null + gợi ý generic.
    /// </summary>
    /// <param name="userId">User cần gợi ý.</param>
    /// <param name="request">Filter options + pagination.</param>
    /// <param name="latitude">Vĩ độ GPS (optional, để tìm cafe gần).</param>
    /// <param name="longitude">Kinh độ GPS (optional, để tìm cafe gần).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Personalized game recommendations với scoring breakdown.</returns>
    Task<SoloPersonalizedResponseDto> SoloPersonalizedDiscoveryAsync(
        Guid userId,
        SoloPersonalizedRequestDto request,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken = default);
}
