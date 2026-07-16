using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IGameTemplateRepository
    {
        Task<PaginatedResponse<GameTemplate>> GetPagedAsync(GetMasterGamesQuery query);
        Task<PaginatedResponse<GameTemplate>> GetBoardGamesPagedAsync(GetMasterGamesQuery query);
        Task<GameTemplate?> GetByIdWithComponentsAsync(Guid id);
        Task<GameTemplate?> GetActiveByIdWithComponentsAsync(Guid id);
        Task<GameTemplate?> GetByIdWithCategoriesForUpdateAsync(Guid id);
        Task<GameTemplate?> GetByIdForUpdateAsync(Guid id);
        Task<bool> ExistsAsync(Guid id);
        Task<Dictionary<Guid, int>> GetComponentCountsByGameIdsAsync(IReadOnlyCollection<Guid> gameIds);
        Task SaveChangesAsync();
    }
}
