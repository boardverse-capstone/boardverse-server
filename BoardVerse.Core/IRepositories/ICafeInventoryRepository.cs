using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ICafeInventoryRepository
    {
        Task<CafeGameInventory?> GetByIdWithDetailsAsync(Guid inventoryId, CancellationToken cancellationToken = default);
        Task<CafeGameInventory?> GetByIdWithDetailsIncludingInactiveAsync(Guid inventoryId, CancellationToken cancellationToken = default);
        Task<CafeGameInventory?> GetByCafeAndGameTemplateAsync(Guid cafeId, Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<CafeGameInventory?> GetByCafeAndGameTemplateIncludingInactiveAsync(Guid cafeId, Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<HashSet<Guid>> GetActiveGameTemplateIdsByCafeAsync(Guid cafeId, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<CafeGameInventory>> GetPagedByCafeAsync(
            Guid cafeId,
            GetCafeInventoryQuery query,
            bool deletedOnly = false, CancellationToken cancellationToken = default);
        Task AddAsync(CafeGameInventory inventory, CancellationToken cancellationToken = default);
        Task SyncInventoryBoxesAsync(Guid inventoryId, CancellationToken cancellationToken = default);
        Task BackfillMissingInventoryBoxesAsync(CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
