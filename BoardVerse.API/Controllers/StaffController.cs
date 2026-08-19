using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/staff")]
    [Authorize(Roles = "CafeStaff")]
    public class StaffController : BaseApiController
    {
        private readonly ICafeService _cafeService;

        public StaffController(ICafeService cafeService)
        {
            _cafeService = cafeService;
        }

        /// <summary>
        /// Lấy danh sách quán mà nhân viên hiện tại đang làm việc (kèm thông tin chi tiết: schedule, pricing, refund policy, operational status; các field SePay raw + managerId được ẩn cho staff). [Role: CafeStaff — yêu cầu đăng nhập với role CafeStaff.]
        /// </summary>
        /// <response code="200">Trả về danh sách quán đang làm việc (có thể rỗng).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có role CafeStaff hoặc bị chặn/vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("my-cafes")]
        [ProducesResponseType(typeof(IEnumerable<ManagerCafeDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMyWorkplaces()
        {
            var currentStaffId = GetUserIdFromClaims();

            var result = await _cafeService.GetMyWorkplacesAsync(currentStaffId);
            return this.NewResponse(200, ApiSuccessMessages.Staff.WorkplacesRetrieved, result);
        }
    }
}
