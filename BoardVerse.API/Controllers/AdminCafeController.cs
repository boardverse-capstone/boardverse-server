using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    /// <summary>
    /// Admin endpoints cho Cafe: CRUD đầy đủ, set operational status.
    /// [Role: Admin]
    /// </summary>
    [ApiController]
    [Route("api/v1/admin/cafes")]
    [Authorize(Roles = "Admin")]
    [Produces("application/json")]
    [Tags("Admin - Cafe")]
    public class AdminCafeController : BaseApiController
    {
        private readonly ICafeService _cafeService;

        public AdminCafeController(ICafeService cafeService)
        {
            _cafeService = cafeService;
        }

        /// <summary>
        /// Lấy danh sách tất cả cafes (phân trang, filter theo status/search).
        /// [Role: Admin]
        /// </summary>
        /// <param name="page">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 20, max 100).</param>
        /// <param name="searchTerm">Tìm kiếm theo tên hoặc địa chỉ.</param>
        /// <param name="status">Filter theo operational status: DATA_BLANK, ACTIVE, INACTIVE, BANNED.</param>
        /// <param name="managerId">Filter theo manager.</param>
        /// <response code="200">Danh sách cafes phân trang.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        [ProducesResponseType(typeof(AdminCafeListResponseDto), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetCafes(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? searchTerm = null,
            [FromQuery] string? status = null,
            [FromQuery] Guid? managerId = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var result = await _cafeService.GetAdminCafesAsync(page, pageSize, searchTerm, status, managerId);
            return NewResponse(200, ApiSuccessMessages.Cafe.ListRetrieved, result);
        }

        /// <summary>
        /// Lấy chi tiết một cafe.
        /// [Role: Admin]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <response code="200">Chi tiết cafe.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Admin.</response>
        /// <response code="404">Không tìm thấy cafe.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{cafeId:guid}")]
        [ProducesResponseType(typeof(AdminCafeDetailDto), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetCafe(Guid cafeId)
        {
            var result = await _cafeService.GetAdminCafeDetailAsync(cafeId);
            if (result == null)
            {
                throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
            }
            return NewResponse(200, ApiSuccessMessages.Cafe.Retrieved, result);
        }

        /// <summary>
        /// Tạo cafe mới (Admin tạo thay manager).
        /// [Role: Admin]
        /// </summary>
        /// <param name="request">Thông tin cafe cần tạo.</param>
        /// <response code="201">Cafe đã được tạo.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Admin.</response>
        /// <response code="404">Không tìm thấy manager.</response>
        /// <response code="409">Manager đã có cafe khác.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost]
        [ProducesResponseType(typeof(AdminCafeDetailDto), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> CreateCafe([FromBody] AdminCreateCafeRequestDto request)
        {
            var result = await _cafeService.AdminCreateCafeAsync(request);
            return NewResponse(201, "Tạo cafe thành công.", result);
        }

        /// <summary>
        /// Cập nhật thông tin cafe (Admin sửa thay manager).
        /// [Role: Admin]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="request">Thông tin cần cập nhật.</param>
        /// <response code="200">Cafe đã được cập nhật.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Admin.</response>
        /// <response code="404">Không tìm thấy cafe.</response>
        /// <response code="409">Cafe đã bị đóng vĩnh viễn hoặc đang bị banned.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut("{cafeId:guid}")]
        [ProducesResponseType(typeof(AdminCafeDetailDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> UpdateCafe(
            Guid cafeId,
            [FromBody] AdminUpdateCafeRequestDto request)
        {
            var result = await _cafeService.AdminUpdateCafeAsync(cafeId, request);
            return NewResponse(200, ApiSuccessMessages.Cafe.Updated, result);
        }

        /// <summary>
        /// Xóa cafe (soft delete - đặt IsActive = false).
        /// [Role: Admin]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <response code="200">Cafe đã được xóa.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Admin.</response>
        /// <response code="404">Không tìm thấy cafe.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpDelete("{cafeId:guid}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> DeleteCafe(Guid cafeId)
        {
            await _cafeService.AdminDeleteCafeAsync(cafeId);
            return NewResponse(200, "Cafe đã được xóa.", new { id = cafeId });
        }

        /// <summary>
        /// Đặt trạng thái vận hành quán đối tác (Admin). [Role: Admin]
        /// </summary>
        /// <param name="cafeId">Mã quán.</param>
        /// <param name="request">status: DATA_BLANK, ACTIVE, INACTIVE, BANNED; reason bắt buộc khi BANNED.</param>
        /// <response code="200">Trạng thái quán đã cập nhật.</response>
        /// <response code="400">status không hợp lệ hoặc thiếu reason khi BANNED.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy quán.</response>
        [HttpPut("{cafeId:guid}/operational-status")]
        [ProducesResponseType(typeof(AdminCafeOperationalStatusResultDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> SetOperationalStatus(
            Guid cafeId,
            [FromBody] AdminSetCafeOperationalStatusRequestDto request)
        {
            var result = await _cafeService.SetOperationalStatusByAdminAsync(cafeId, request);
            return NewResponse(200, ApiSuccessMessages.Cafe.OperationalStatusUpdated, result);
        }
    }
}
