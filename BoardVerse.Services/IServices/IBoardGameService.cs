using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;

namespace BoardVerse.Services.IServices
{
    public interface IBoardGameService
    {
        Task<PaginatedResponse<BoardGameListItemDto>> SearchBoardGamesAsync(GetBoardGamesQuery query);
        Task<BoardGameDetailDto> GetBoardGameDetailsAsync(Guid id);
        Task<BoardGameDetailDto> GetBoardGameByIdAsync(Guid id);
        Task<List<CategoryDto>> GetCategoriesAsync();
    }
}
