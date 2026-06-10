using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;
using BoardVerse.Core.Enum;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/cafes/{cafeId:guid}/inventory")]
    public class CafeInventoryController : BaseApiController
    {
        private readonly ICafeInventoryService _inventoryService;

        public CafeInventoryController(ICafeInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// Thêm board game từ danh mục master vào kho quán. [Role: Manager — phải là chủ quán (ManagerId của cafe).]
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> AddToInventory(Guid cafeId, [FromBody] AddCafeInventoryRequestDto dto)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.AddToInventoryAsync(cafeId, managerId, dto);
            return this.NewResponse(201, "Game added to inventory successfully", result);
        }

        /// <summary>
        /// Danh sách kho game — Public/User (browse), CafeStaff/Manager (full + penalties).
        /// Manager có thể lọc theo tên, status và sắp xếp.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetInventory(
            Guid cafeId,
            [FromQuery] string? searchTerm,
            [FromQuery] CafeGameInventoryStatus? status,
            [FromQuery] InventorySortField sortBy = InventorySortField.UpdatedAt,
            [FromQuery] bool sortDescending = true,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var (viewerId, viewerRole) = GetOptionalViewerContext();
            var query = new GetCafeInventoryQuery
            {
                SearchTerm = searchTerm,
                Status = status,
                SortBy = sortBy,
                SortDescending = sortDescending,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            var result = await _inventoryService.GetInventoryForViewerAsync(
                cafeId, viewerId, viewerRole, query);
            return this.NewResponse(200, "Inventory retrieved successfully", result);
        }

        /// <summary>
        /// Danh sách mục kho đã xóa (soft delete). [Role: Manager — phải là chủ quán.]
        /// </summary>
        [HttpGet("deleted")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> GetDeletedInventory(
            Guid cafeId,
            [FromQuery] string? searchTerm,
            [FromQuery] CafeGameInventoryStatus? status,
            [FromQuery] InventorySortField sortBy = InventorySortField.UpdatedAt,
            [FromQuery] bool sortDescending = true,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var managerId = GetUserIdFromClaims();
            var query = new GetCafeInventoryQuery
            {
                SearchTerm = searchTerm,
                Status = status,
                SortBy = sortBy,
                SortDescending = sortDescending,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            var result = await _inventoryService.GetDeletedInventoryAsync(cafeId, managerId, query);
            return this.NewResponse(200, "Deleted inventory retrieved successfully", result);
        }

        /// <summary>
        /// Chi tiết mục kho — Public/User (browse), CafeStaff/Manager (full + penalties).
        /// </summary>
        [HttpGet("{inventoryId:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetInventoryItem(Guid cafeId, Guid inventoryId)
        {
            var (viewerId, viewerRole) = GetOptionalViewerContext();
            var result = await _inventoryService.GetInventoryItemForViewerAsync(
                cafeId, inventoryId, viewerId, viewerRole);
            return this.NewResponse(200, "Inventory item retrieved successfully", result);
        }

        /// <summary>
        /// Cập nhật số hộp, trạng thái hoặc phí phạt linh kiện. [Role: Manager — phải là chủ quán.]
        /// </summary>
        [HttpPut("{inventoryId:guid}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> UpdateInventory(
            Guid cafeId,
            Guid inventoryId,
            [FromBody] UpdateCafeInventoryRequestDto dto)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.UpdateInventoryAsync(cafeId, inventoryId, managerId, dto);
            return this.NewResponse(200, "Inventory updated successfully", result);
        }

        /// <summary>
        /// Khôi phục mục kho đã xóa mềm. [Role: Manager — phải là chủ quán.]
        /// </summary>
        [HttpPost("{inventoryId:guid}/restore")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> RestoreInventory(Guid cafeId, Guid inventoryId)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.RestoreInventoryAsync(cafeId, inventoryId, managerId);
            return this.NewResponse(200, "Inventory item restored successfully", result);
        }

        /// <summary>
        /// Đồng bộ phí phạt linh kiện từ master game (thêm component mới với phí 0). [Role: Manager — phải là chủ quán.]
        /// </summary>
        [HttpPost("{inventoryId:guid}/sync-penalties")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> SyncPenalties(Guid cafeId, Guid inventoryId)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.SyncPenaltiesAsync(cafeId, inventoryId, managerId);
            return this.NewResponse(200, "Component penalties synced successfully", result);
        }

        /// <summary>
        /// Xóa game khỏi kho quán (soft delete). [Role: Manager — phải là chủ quán.]
        /// </summary>
        [HttpDelete("{inventoryId:guid}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> RemoveFromInventory(Guid cafeId, Guid inventoryId)
        {
            var managerId = GetUserIdFromClaims();
            await _inventoryService.RemoveFromInventoryAsync(cafeId, inventoryId, managerId);
            return this.NewResponse(200, "Game removed from inventory successfully", null);
        }
    }
}
