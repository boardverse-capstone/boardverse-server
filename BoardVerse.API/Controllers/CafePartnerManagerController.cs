using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/cafe-partner/me")]
    [Authorize(Roles = "Manager")]
    public class CafePartnerManagerController : BaseApiController
    {
        private readonly ICafePartnerApplicationService _service;

        public CafePartnerManagerController(ICafePartnerApplicationService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lấy hồ sơ đối tác của Manager đang đăng nhập (Web POS). [Role: Manager]
        /// </summary>
        /// <response code="200">Trả về hồ sơ đối tác và trạng thái kích hoạt.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có hồ sơ đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.GetMyPartnerProfileAsync(managerId);
            return NewResponse(200, "Partner profile retrieved successfully", result);
        }

        /// <summary>
        /// Cập nhật hồ sơ vận hành (Giai đoạn 2) trước khi kích hoạt. [Role: Manager]
        /// </summary>
        /// <param name="request">Hạ tầng, catalog game, sơ đồ bàn.</param>
        /// <response code="200">Cập nhật hồ sơ vận hành thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc quán đang ACTIVE (cần tạm dừng trước).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có hồ sơ đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut("operational-profile")]
        public async Task<IActionResult> UpdateOperationalProfile([FromBody] UpdateOperationalProfileRequestDto request)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.UpdateOperationalProfileAsync(managerId, request);
            return NewResponse(200, "Operational profile updated successfully", result);
        }

        /// <summary>
        /// Kích hoạt quán (DATA_BLANK → ACTIVE) khi đủ điều kiện ràng buộc. [Role: Manager]
        /// </summary>
        /// <response code="200">Kích hoạt quán thành công, hiển thị trên Mobile App.</response>
        /// <response code="400">Chưa đủ điều kiện (≥5 bàn, ≥20 game, ≥3 ảnh, sơ đồ bàn) hoặc trạng thái không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có hồ sơ đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("activate")]
        public async Task<IActionResult> Activate()
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.ActivateAsync(managerId);
            return NewResponse(200, "Cafe activated successfully", result);
        }

        /// <summary>
        /// Tạm dừng hoạt động (ACTIVE → DATA_BLANK). [Role: Manager]
        /// </summary>
        /// <response code="200">Tạm dừng quán thành công, ẩn khỏi Mobile App.</response>
        /// <response code="400">Còn phiên đặt bàn đang chạy hoặc trạng thái không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có hồ sơ đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("deactivate")]
        public async Task<IActionResult> Deactivate()
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.DeactivateAsync(managerId);
            return NewResponse(200, "Cafe paused successfully", result);
        }
    }
}
