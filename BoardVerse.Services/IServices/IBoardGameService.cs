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
        Task<GamePlayConfigurationDto> GetPlayConfigurationAsync(Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<GamePlayNavigationResponseDto> ResolvePlayNavigationAsync(
            Guid gameTemplateId,
            ResolveGamePlayNavigationRequestDto request, CancellationToken cancellationToken = default);
    }
}

