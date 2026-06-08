using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;

namespace BoardVerse.Services.IServices
{
    public interface ICafeInventoryService
    {
        Task<CafeInventoryResponseDto> AddToInventoryAsync(Guid cafeId, Guid managerId, AddCafeInventoryRequestDto dto);
        Task<object> GetInventoryForViewerAsync(
            Guid cafeId,
            Guid? viewerId,
            string? viewerRole,
            PaginationParams paginationParams);
        Task<object> GetInventoryItemForViewerAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid? viewerId,
            string? viewerRole);
        Task<CafeInventoryResponseDto> UpdateInventoryAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid managerId,
            UpdateCafeInventoryRequestDto dto);
        Task RemoveFromInventoryAsync(Guid cafeId, Guid inventoryId, Guid managerId);
    }
}
