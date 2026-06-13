using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;

namespace BoardVerse.Services.IServices
{
    public interface IGameTemplateService
    {
        Task<PaginatedResponse<MasterGameResponseDto>> GetMasterGamesAsync(GetMasterGamesQuery query);
        Task<MasterGameResponseDto> GetMasterGameByIdAsync(Guid id, Guid? cafeId = null);
    }
}
