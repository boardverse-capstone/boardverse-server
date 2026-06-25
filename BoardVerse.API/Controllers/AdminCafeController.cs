using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/admin/cafes")]
    [Authorize(Roles = "Admin")]
    public class AdminCafeController : BaseApiController
    {
        private readonly ICafeService _cafeService;

        public AdminCafeController(ICafeService cafeService)
        {
            _cafeService = cafeService;
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
        public async Task<IActionResult> SetOperationalStatus(
            Guid cafeId,
            [FromBody] AdminSetCafeOperationalStatusRequestDto request)
        {
            var result = await _cafeService.SetOperationalStatusByAdminAsync(cafeId, request);
            return NewResponse(200, ApiSuccessMessages.Cafe.OperationalStatusUpdated, result);
        }
    }
}
