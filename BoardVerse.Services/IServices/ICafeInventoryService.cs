using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface ICafeInventoryService
    {
        Task<CafeInventoryResponseDto> AddToInventoryAsync(Guid cafeId, Guid managerId, AddCafeInventoryRequestDto dto, CancellationToken cancellationToken = default);
        Task<object> GetInventoryForViewerAsync(
            Guid cafeId,
            Guid? viewerId,
            string? viewerRole,
            GetCafeInventoryQuery query, CancellationToken cancellationToken = default);
        Task<object> GetInventoryItemForViewerAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid? viewerId,
            string? viewerRole, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<CafeInventoryResponseDto>> GetDeletedInventoryAsync(
            Guid cafeId,
            Guid managerId,
            GetCafeInventoryQuery query, CancellationToken cancellationToken = default);
        Task<CafeInventoryResponseDto> UpdateInventoryAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid managerId,
            UpdateCafeInventoryRequestDto dto, CancellationToken cancellationToken = default);
        Task<CafeInventoryResponseDto> RestoreInventoryAsync(Guid cafeId, Guid inventoryId, Guid managerId);
        Task<CafeInventoryResponseDto> SyncPenaltiesAsync(Guid cafeId, Guid inventoryId, Guid managerId);
        Task<CafeInventoryResponseDto> SyncBoxesAsync(Guid cafeId, Guid inventoryId, Guid managerId);
        Task RemoveFromInventoryAsync(Guid cafeId, Guid inventoryId, Guid managerId);
    }
}
