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
            GetCafeInventoryQuery query);
        Task<object> GetInventoryItemForViewerAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid? viewerId,
            string? viewerRole);
        Task<PaginatedResponse<CafeInventoryResponseDto>> GetDeletedInventoryAsync(
            Guid cafeId,
            Guid managerId,
            GetCafeInventoryQuery query);
        Task<CafeInventoryResponseDto> UpdateInventoryAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid managerId,
            UpdateCafeInventoryRequestDto dto);
        Task<CafeInventoryResponseDto> RestoreInventoryAsync(Guid cafeId, Guid inventoryId, Guid managerId);
        Task<CafeInventoryResponseDto> SyncPenaltiesAsync(Guid cafeId, Guid inventoryId, Guid managerId);
        Task RemoveFromInventoryAsync(Guid cafeId, Guid inventoryId, Guid managerId);
    }
}
