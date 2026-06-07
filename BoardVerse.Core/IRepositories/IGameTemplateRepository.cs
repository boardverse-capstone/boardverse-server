using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IGameTemplateRepository
    {
        Task<PaginatedResponse<GameTemplate>> GetPagedAsync(GetMasterGamesQuery query);
    }
}
