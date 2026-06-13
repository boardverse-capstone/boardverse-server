using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/cafe-partner-applications")]
    public class CafePartnerApplicationController : BaseApiController
    {
        private readonly ICafePartnerApplicationService _service;

        public CafePartnerApplicationController(ICafePartnerApplicationService service)
        {
            _service = service;
        }

        /// <summary>
        /// Gửi đơn đăng ký hợp tác quán cafe (Giai đoạn 1 — Landing Page). [Role: Public]
        /// </summary>
        /// <param name="request">Thông tin quán và người đại diện.</param>
        /// <response code="201">Đơn đã gửi thành công (PENDING_APPROVAL).</response>
        /// <response code="400">Thiếu trường bắt buộc hoặc dữ liệu không hợp lệ (hotline, giờ làm việc, ảnh giấy phép).</response>
        /// <response code="409">Trùng MST/địa chỉ 100%, email đã có đơn mở, hoặc email thuộc Admin/Manager/CafeStaff.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Submit([FromBody] SubmitCafePartnerApplicationRequestDto request)
        {
            var (userId, role) = GetOptionalViewerContext();
            var submittedByUserId = string.Equals(role, "Player", StringComparison.OrdinalIgnoreCase) ? userId : null;
            var result = await _service.SubmitAsync(request, submittedByUserId);
            return NewResponse(201, "Application submitted successfully", result);
        }

        /// <summary>
        /// Tra cứu trạng thái đơn theo ID. [Role: Public]
        /// </summary>
        /// <param name="id">Mã đơn đăng ký.</param>
        /// <response code="200">Trả về thông tin đơn.</response>
        /// <response code="404">Không tìm thấy đơn.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _service.GetByIdAsync(id);
            return NewResponse(200, "Application retrieved successfully", result);
        }
    }
}
