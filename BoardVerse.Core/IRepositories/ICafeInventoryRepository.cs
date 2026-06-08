using BoardVerse.Core.Common;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafeInventoryRepository
    {
        Task<CafeGameInventory?> GetByIdWithDetailsAsync(Guid inventoryId);
        Task<CafeGameInventory?> GetByCafeAndGameTemplateAsync(Guid cafeId, Guid gameTemplateId);
        Task<PaginatedResponse<CafeGameInventory>> GetPagedByCafeAsync(Guid cafeId, PaginationParams paginationParams);
        Task AddAsync(CafeGameInventory inventory);
        Task SaveChangesAsync();
    }
}
