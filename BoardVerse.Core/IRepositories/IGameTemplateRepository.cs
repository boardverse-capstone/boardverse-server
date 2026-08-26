using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IGameTemplateRepository
    {
        Task<PaginatedResponse<GameTemplate>> GetPagedAsync(GetMasterGamesQuery query, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<GameTemplate>> GetBoardGamesPagedAsync(GetMasterGamesQuery query, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByIdWithComponentsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetActiveByIdWithComponentsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByIdWithCategoriesForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Dictionary<Guid, int>> GetComponentCountsByGameIdsAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Kiểm tra cafe có trong kho (CafeGameInventory) game này không.
        /// </summary>
        Task<bool> CafeHasGameAsync(Guid cafeId, Guid gameTemplateId, CancellationToken cancellationToken = default);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
