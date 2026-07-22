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
        Task<GameTemplate?> GetByIdAsync(Guid id);
        Task<GameTemplate?> GetByNameAsync(string name);
        Task<bool> ExistsAsync(Guid id);
        Task<Dictionary<Guid, int>> GetComponentCountsByGameIdsAsync(IReadOnlyCollection<Guid> gameIds);

        /// <summary>
        /// Kiểm tra cafe có trong kho (CafeGameInventory) game này không.
        /// </summary>
        Task<bool> CafeHasGameAsync(Guid cafeId, Guid gameTemplateId);

        Task SaveChangesAsync();
    }
}
