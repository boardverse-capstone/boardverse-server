using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;
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
        /// <param name="dto">GameTemplateId, số hộp, trạng thái (chuỗi enum, vd. "Available"), phí phạt linh kiện tùy chọn.</param>
        /// <response code="201">Thêm game vào kho thành công, trả về mục kho đầy đủ (có penalties).</response>
        /// <response code="400">GameTemplateId thiếu; boxQuantity ngoài 1–1000; status enum không hợp lệ; component ID không thuộc game.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải role Manager, hoặc Manager không phải chủ quán.</response>
        /// <response code="404">Không tìm thấy quán hoặc master game.</response>
        /// <response code="409">Game đã có trong kho quán — dùng PUT để cập nhật.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> AddToInventory(Guid cafeId, [FromBody] AddCafeInventoryRequestDto dto)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _inventoryService.AddToInventoryAsync(cafeId, managerId, dto);
            return this.NewResponse(201, "Game added to inventory successfully", result);
        }

        /// <summary>
        /// Danh sách kho game — Public/User (browse), CafeStaff/Manager (full + penalties). [Role: Public / User / CafeStaff / Manager]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Số bản ghi mỗi trang (mặc định 10).</param>
        /// <response code="200">Public/User: danh sách browse (không penalties). CafeStaff thuộc quán hoặc Manager chủ quán: danh sách đầy đủ.</response>
        /// <response code="404">Không tìm thấy quán hoặc quán đã bị vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetInventory(
            Guid cafeId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var (viewerId, viewerRole) = GetOptionalViewerContext();
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await _inventoryService.GetInventoryForViewerAsync(
                cafeId, viewerId, viewerRole, pagination);
            return this.NewResponse(200, "Inventory retrieved successfully", result);
        }

        /// <summary>
        /// Chi tiết mục kho — Public/User (browse), CafeStaff/Manager (full + penalties). [Role: Public / User / CafeStaff / Manager]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="inventoryId">Mã mục kho.</param>
        /// <response code="200">Public/User: chi tiết browse. CafeStaff thuộc quán hoặc Manager chủ quán: chi tiết đầy đủ kèm penalties.</response>
        /// <response code="404">Không tìm thấy quán hoặc mục kho không thuộc quán này.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
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
        /// Cập nhật số hộp, trạng thái hoặc phí phạt linh kiện. [Role: Manager — phải là chủ quán (ManagerId của cafe).]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="inventoryId">Mã mục kho.</param>
        /// <param name="dto">Chỉ gửi field cần cập nhật.</param>
        /// <response code="200">Cập nhật mục kho thành công, trả về dữ liệu đầy đủ.</response>
        /// <response code="400">Component ID không thuộc game; dữ liệu đầu vào không hợp lệ.</response>
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
            return this.NewResponse(200, "Inventory updated successfully", result);
        }

        /// <summary>
        /// Xóa game khỏi kho quán (soft delete). [Role: Manager — phải là chủ quán (ManagerId của cafe).]
        /// </summary>
        /// <param name="cafeId">Mã định danh quán cafe.</param>
        /// <param name="inventoryId">Mã mục kho.</param>
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
            return this.NewResponse(200, "Game removed from inventory successfully", null);
        }
    }
}
