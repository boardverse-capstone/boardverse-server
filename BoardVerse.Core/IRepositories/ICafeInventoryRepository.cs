using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafeInventoryRepository
    {
        Task<CafeGameInventory?> GetByIdWithDetailsAsync(Guid inventoryId);
        Task<CafeGameInventory?> GetByIdWithDetailsIncludingInactiveAsync(Guid inventoryId);
        Task<CafeGameInventory?> GetByCafeAndGameTemplateAsync(Guid cafeId, Guid gameTemplateId);
        Task<CafeGameInventory?> GetByCafeAndGameTemplateIncludingInactiveAsync(Guid cafeId, Guid gameTemplateId);
        Task<HashSet<Guid>> GetActiveGameTemplateIdsByCafeAsync(Guid cafeId);
        Task<PaginatedResponse<CafeGameInventory>> GetPagedByCafeAsync(
            Guid cafeId,
            GetCafeInventoryQuery query,
            bool deletedOnly = false);
        Task AddAsync(CafeGameInventory inventory);
        Task SaveChangesAsync();
    }
}
