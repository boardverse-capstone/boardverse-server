using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/admin/cafe-partner-applications")]
    [Authorize(Roles = "Admin")]
    public class AdminCafePartnerApplicationController : BaseApiController
    {
        private readonly ICafePartnerApplicationService _service;

        public AdminCafePartnerApplicationController(ICafePartnerApplicationService service)
        {
            _service = service;
        }

        /// <summary>
        /// Danh sách đơn đăng ký cafe partner (Admin). [Role: Admin]
        /// </summary>
        /// <param name="query">Lọc theo search, status, phân trang.</param>
        /// <response code="200">Danh sách đơn đăng ký.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] AdminCafePartnerApplicationQueryDto query)
        {
            var result = await _service.GetAllForAdminAsync(query);
            return NewResponse(200, ApiSuccessMessages.CafePartner.ApplicationsRetrieved, result);
        }

        /// <summary>
        /// Chi tiết đơn đăng ký. [Role: Admin]
        /// </summary>
        /// <param name="id">Mã đơn đăng ký.</param>
        /// <response code="200">Chi tiết đơn.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy đơn.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _service.GetByIdAsync(id);
            return NewResponse(200, ApiSuccessMessages.CafePartner.ApplicationRetrieved, result);
        }

        /// <summary>
        /// Phê duyệt đơn — tạo tài khoản Manager và quán (DATA_BLANK). [Role: Admin]
        /// </summary>
        /// <param name="id">Mã đơn đăng ký.</param>
        /// <response code="200">Duyệt thành công — tạo Manager + Cafe (DATA_BLANK), gửi email credentials.</response>
        /// <response code="400">Trạng thái đơn không hợp lệ hoặc thiếu ảnh giấy phép kinh doanh.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy đơn.</response>
        /// <response code="409">Email không đủ điều kiện hoặc đã quản lý quán khác.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{id:guid}/approve")]
        public async Task<IActionResult> Approve(Guid id)
        {
            var adminId = GetUserIdFromClaims();
            var result = await _service.ApproveAsync(id, adminId);
            return NewResponse(200, ApiSuccessMessages.CafePartner.ApplicationApproved, result);
        }

        /// <summary>
        /// Từ chối đơn đăng ký. [Role: Admin]
        /// </summary>
        /// <param name="id">Mã đơn đăng ký.</param>
        /// <param name="request">Lý do từ chối (bắt buộc).</param>
        /// <response code="200">Từ chối đơn thành công, gửi email thông báo.</response>
        /// <response code="400">Thiếu lý do từ chối hoặc trạng thái đơn không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy đơn.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{id:guid}/reject")]
        public async Task<IActionResult> Reject(Guid id, [FromBody] RejectCafePartnerApplicationRequestDto request)
        {
            var adminId = GetUserIdFromClaims();
            var result = await _service.RejectAsync(id, adminId, request);
            return NewResponse(200, ApiSuccessMessages.CafePartner.ApplicationRejected, result);
        }
    }
}
