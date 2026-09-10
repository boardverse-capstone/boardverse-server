using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IBoardGameService
    {
        Task<PaginatedResponse<BoardGameListItemDto>> SearchBoardGamesAsync(GetBoardGamesQuery query, CancellationToken cancellationToken = default);
        Task<BoardGameDetailDto> GetBoardGameByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Top N board game được chơi nhiều nhất trong hệ thống (sắp xếp giảm dần theo PlayCount).
        /// Dùng cho widget UI mobile bên player.
        /// </summary>
        /// <param name="topCount">Số lượng game trả về (mặc định 5).</param>
        /// <param name="cancellationToken">Token hủy.</param>
        /// <returns>Danh sách TopBoardGameDto.</returns>
        Task<List<TopBoardGameDto>> GetTopPlayedBoardGamesAsync(int topCount = 5, CancellationToken cancellationToken = default);

        Task<GamePlayConfigurationDto> GetPlayConfigurationAsync(Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<GamePlayNavigationResponseDto> ResolvePlayNavigationAsync(
            Guid gameTemplateId,
            ResolveGamePlayNavigationRequestDto request, CancellationToken cancellationToken = default);
    }
}

