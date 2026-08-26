using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IGameTemplateService
    {
        Task<PaginatedResponse<MasterGameResponseDto>> GetMasterGamesAsync(GetMasterGamesQuery query, CancellationToken cancellationToken = default);
        Task<MasterGameResponseDto> GetMasterGameByIdAsync(Guid id, Guid? cafeId = null, CancellationToken cancellationToken = default);
    }
}
