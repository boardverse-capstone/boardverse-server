using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminMasterCatalogController : BaseApiController
    {
        private readonly IAdminMasterCatalogService _catalogService;

        public AdminMasterCatalogController(IAdminMasterCatalogService catalogService)
        {
            _catalogService = catalogService;
        }

        /// <summary>
        /// Danh sách thể loại board game (Admin — gồm cả inactive khi includeInactive=true). [Role: Admin]
        /// </summary>
        /// <param name="includeInactive">Khi true, trả cả thể loại đã vô hiệu hóa.</param>
        /// <response code="200">Danh sách thể loại.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories([FromQuery] bool includeInactive = false)
        {
            var result = await _catalogService.GetCategoriesAsync(includeInactive);
            return NewResponse(200, ApiSuccessMessages.AdminCatalog.CategoriesRetrieved, result);
        }

        /// <summary>
        /// Tạo thể loại board game mới. [Role: Admin]
        /// </summary>
        /// <param name="request">name (bắt buộc), slug (tuỳ chọn — tự sinh từ name), description, sortOrder.</param>
        /// <response code="201">Thể loại đã tạo.</response>
        /// <response code="400">Dữ liệu request không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="409">Slug đã tồn tại.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("categories")]
        public async Task<IActionResult> CreateCategory([FromBody] AdminCreateCategoryRequestDto request)
        {
            var result = await _catalogService.CreateCategoryAsync(request);
            return NewResponse(201, ApiSuccessMessages.AdminCatalog.CategoryCreated, result);
        }

        /// <summary>
        /// Cập nhật thể loại board game. [Role: Admin]
        /// </summary>
        /// <param name="id">Mã thể loại.</param>
        /// <param name="request">Các field cần đổi (name, slug, description, sortOrder, isActive).</param>
        /// <response code="200">Thể loại đã cập nhật.</response>
        /// <response code="400">Dữ liệu request không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy thể loại.</response>
        /// <response code="409">Slug trùng thể loại khác.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut("categories/{id:guid}")]
        public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] AdminUpdateCategoryRequestDto request)
        {
            var result = await _catalogService.UpdateCategoryAsync(id, request);
            return NewResponse(200, ApiSuccessMessages.AdminCatalog.CategoryUpdated, result);
        }

        /// <summary>
        /// Vô hiệu hóa thể loại (soft delete — isActive=false). [Role: Admin]
        /// </summary>
        /// <param name="id">Mã thể loại.</param>
        /// <response code="200">Thể loại đã vô hiệu hóa.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy thể loại.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpDelete("categories/{id:guid}")]
        public async Task<IActionResult> DeleteCategory(Guid id)
        {
            var result = await _catalogService.DeleteCategoryAsync(id);
            return NewResponse(200, ApiSuccessMessages.AdminCatalog.CategoryDeactivated, result);
        }

        /// <summary>
        /// Liệt kê linh kiện của một game master. [Role: Admin]
        /// </summary>
        /// <param name="gameTemplateId">Mã GameTemplate.</param>
        /// <response code="200">Danh sách linh kiện.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy game.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("master-games/{gameTemplateId:guid}/components")]
        public async Task<IActionResult> GetGameComponents(Guid gameTemplateId)
        {
            var result = await _catalogService.GetGameComponentsAsync(gameTemplateId);
            return NewResponse(200, ApiSuccessMessages.AdminCatalog.ComponentsRetrieved, result);
        }

        /// <summary>
        /// Thêm linh kiện vào game master. [Role: Admin]
        /// </summary>
        /// <param name="gameTemplateId">Mã GameTemplate.</param>
        /// <param name="request">componentName, componentKind (tuỳ chọn), defaultQuantity.</param>
        /// <response code="201">Linh kiện đã tạo.</response>
        /// <response code="400">Dữ liệu request không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy game.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("master-games/{gameTemplateId:guid}/components")]
        public async Task<IActionResult> CreateGameComponent(
            Guid gameTemplateId,
            [FromBody] AdminCreateGameComponentRequestDto request)
        {
            var result = await _catalogService.CreateGameComponentAsync(gameTemplateId, request);
            return NewResponse(201, ApiSuccessMessages.AdminCatalog.ComponentCreated, result);
        }

        /// <summary>
        /// Cập nhật linh kiện trên game master. [Role: Admin]
        /// </summary>
        /// <param name="gameTemplateId">Mã GameTemplate.</param>
        /// <param name="componentId">Mã linh kiện.</param>
        /// <param name="request">componentName, componentKind, defaultQuantity (chỉ gửi field cần đổi).</param>
        /// <response code="200">Linh kiện đã cập nhật.</response>
        /// <response code="400">Dữ liệu request không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy game hoặc linh kiện.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut("master-games/{gameTemplateId:guid}/components/{componentId:guid}")]
        public async Task<IActionResult> UpdateGameComponent(
            Guid gameTemplateId,
            Guid componentId,
            [FromBody] AdminUpdateGameComponentRequestDto request)
        {
            var result = await _catalogService.UpdateGameComponentAsync(gameTemplateId, componentId, request);
            return NewResponse(200, ApiSuccessMessages.AdminCatalog.ComponentUpdated, result);
        }

        /// <summary>
        /// Xóa linh kiện khỏi game master (bị chặn nếu đang có phí phạt kho quán). [Role: Admin]
        /// </summary>
        /// <param name="gameTemplateId">Mã GameTemplate.</param>
        /// <param name="componentId">Mã linh kiện.</param>
        /// <response code="200">Linh kiện đã xóa.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy game hoặc linh kiện.</response>
        /// <response code="409">Linh kiện đang được dùng trong inventory penalties.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpDelete("master-games/{gameTemplateId:guid}/components/{componentId:guid}")]
        public async Task<IActionResult> DeleteGameComponent(Guid gameTemplateId, Guid componentId)
        {
            await _catalogService.DeleteGameComponentAsync(gameTemplateId, componentId);
            return NewResponse(200, ApiSuccessMessages.AdminCatalog.ComponentDeleted, null);
        }

        /// <summary>
        /// Lấy thể loại đang gán cho game master. [Role: Admin]
        /// </summary>
        /// <param name="gameTemplateId">Mã GameTemplate.</param>
        /// <response code="200">Danh sách thể loại.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy game.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("master-games/{gameTemplateId:guid}/categories")]
        public async Task<IActionResult> GetGameCategories(Guid gameTemplateId)
        {
            var result = await _catalogService.GetGameCategoriesAsync(gameTemplateId);
            return NewResponse(200, ApiSuccessMessages.AdminCatalog.GameCategoriesRetrieved, result);
        }

        /// <summary>
        /// Gán lại toàn bộ thể loại cho game master (thay thế danh sách hiện tại). [Role: Admin]
        /// </summary>
        /// <param name="gameTemplateId">Mã GameTemplate.</param>
        /// <param name="request">categoryIds — mảng GUID thể loại active.</param>
        /// <response code="200">Thể loại đã cập nhật.</response>
        /// <response code="400">Một hoặc nhiều categoryId không tồn tại hoặc inactive.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy game.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut("master-games/{gameTemplateId:guid}/categories")]
        public async Task<IActionResult> SetGameCategories(
            Guid gameTemplateId,
            [FromBody] AdminSetGameCategoriesRequestDto request)
        {
            var result = await _catalogService.SetGameCategoriesAsync(gameTemplateId, request);
            return NewResponse(200, ApiSuccessMessages.AdminCatalog.GameCategoriesUpdated, result);
        }
    }
}
