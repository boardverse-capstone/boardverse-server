using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
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
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="dto">gameTemplateId, boxQuantity, status và componentPenalties (tùy chọn).</param>
        /// <response code="201">Thêm game vào kho thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc component ID không thuộc game đã chọn.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán hoặc master game.</response>
        /// <response code="409">Game đã có trong kho; hoặc đã xóa mềm — dùng restore thay vì thêm mới.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> AddToInventory(Guid cafeId, [FromBody] AddCafeInventoryRequestDto dto)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.AddToInventoryAsync(cafeId, managerId, dto);
            return this.NewResponse(201, ApiSuccessMessages.Inventory.GameAdded, result);
        }

        /// <summary>
        /// Danh sách kho game của quán. [Role: Public/Player — browse; CafeStaff/Manager — full kèm phí phạt.]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="searchTerm">Từ khóa fuzzy search theo tên game (bỏ dấu, hỗ trợ alias).</param>
        /// <param name="status">Lọc trạng thái: Available, InUse, Damaged, Maintenance, Retired.</param>
        /// <param name="sortBy">Trường sắp xếp (mặc định UpdatedAt).</param>
        /// <param name="sortDescending">Sắp xếp giảm dần (mặc định true).</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 10).</param>
        /// <response code="200">Trả về danh sách kho có phân trang (browse hoặc full tùy role).</response>
        /// <response code="404">Không tìm thấy quán hoặc quán đã bị vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
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
            return this.NewResponse(200, ApiSuccessMessages.Inventory.Retrieved, result);
        }

        /// <summary>
        /// Danh sách mục kho đã xóa mềm (soft delete). [Role: Manager — phải là chủ quán.]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="searchTerm">Từ khóa fuzzy search theo tên game.</param>
        /// <param name="status">Lọc trạng thái khi xóa.</param>
        /// <param name="sortBy">Trường sắp xếp (mặc định UpdatedAt).</param>
        /// <param name="sortDescending">Sắp xếp giảm dần (mặc định true).</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 10).</param>
        /// <response code="200">Trả về danh sách mục kho đã xóa có phân trang.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
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
            return this.NewResponse(200, ApiSuccessMessages.Inventory.DeletedRetrieved, result);
        }

        /// <summary>
        /// Chi tiết một mục kho. [Role: Public/Player — browse; CafeStaff/Manager — full kèm phí phạt.]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="inventoryId">Mã mục kho (CafeGameInventory.Id).</param>
        /// <response code="200">Trả về chi tiết mục kho (browse hoặc full tùy role).</response>
        /// <response code="404">Không tìm thấy quán hoặc mục kho.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{inventoryId:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetInventoryItem(Guid cafeId, Guid inventoryId)
        {
            var (viewerId, viewerRole) = GetOptionalViewerContext();
            var result = await _inventoryService.GetInventoryItemForViewerAsync(
                cafeId, inventoryId, viewerId, viewerRole);
            return this.NewResponse(200, ApiSuccessMessages.Inventory.ItemRetrieved, result);
        }

        /// <summary>
        /// Cập nhật số hộp, trạng thái hoặc phí phạt linh kiện. [Role: Manager — phải là chủ quán.]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="inventoryId">Mã mục kho cần cập nhật.</param>
        /// <param name="dto">boxQuantity, status và/hoặc componentPenalties (chỉ gửi field muốn đổi).</param>
        /// <response code="200">Cập nhật mục kho thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc component ID không thuộc game.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán hoặc mục kho.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut("{inventoryId:guid}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> UpdateInventory(
            Guid cafeId,
            Guid inventoryId,
            [FromBody] UpdateCafeInventoryRequestDto dto)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.UpdateInventoryAsync(cafeId, inventoryId, managerId, dto);
            return this.NewResponse(200, ApiSuccessMessages.Inventory.Updated, result);
        }

        /// <summary>
        /// Khôi phục mục kho đã xóa mềm. [Role: Manager — phải là chủ quán.]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="inventoryId">Mã mục kho đã xóa mềm cần khôi phục.</param>
        /// <response code="200">Khôi phục mục kho thành công.</response>
        /// <response code="400">Mục kho đang active — không cần restore.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán hoặc mục kho.</response>
        /// <response code="409">Xung đột khi khôi phục (vd. game master đã thay đổi).</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{inventoryId:guid}/restore")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> RestoreInventory(Guid cafeId, Guid inventoryId)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.RestoreInventoryAsync(cafeId, inventoryId, managerId);
            return this.NewResponse(200, ApiSuccessMessages.Inventory.Restored, result);
        }

        /// <summary>
        /// Đồng bộ phí phạt linh kiện từ master game (thêm component mới với phí 0). [Role: Manager — phải là chủ quán.]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="inventoryId">Mã mục kho cần đồng bộ phí phạt.</param>
        /// <response code="200">Đồng bộ phí phạt linh kiện thành công.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán, mục kho hoặc master game.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{inventoryId:guid}/sync-penalties")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> SyncPenalties(Guid cafeId, Guid inventoryId)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.SyncPenaltiesAsync(cafeId, inventoryId, managerId);
            return this.NewResponse(200, ApiSuccessMessages.Inventory.PenaltiesSynced, result);
        }

        /// <summary>
        /// Xóa game khỏi kho quán (soft delete). [Role: Manager — phải là chủ quán.]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="inventoryId">Mã mục kho cần xóa.</param>
        /// <response code="200">Xóa mềm mục kho thành công.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán hoặc mục kho.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpDelete("{inventoryId:guid}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> RemoveFromInventory(Guid cafeId, Guid inventoryId)
        {
            var managerId = GetUserIdFromClaims();
            await _inventoryService.RemoveFromInventoryAsync(cafeId, inventoryId, managerId);
            return this.NewResponse(200, ApiSuccessMessages.Inventory.GameRemoved, null);
        }
    }
}
