using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/manager/cafes/me")]
    [Authorize(Roles = "Manager")]
    [Produces("application/json")]
    [Tags("Manager - Cafe")]
    public class ManagerCafeProfileController : BaseApiController
    {
        private readonly ICafePartnerApplicationService _service;

        public ManagerCafeProfileController(ICafePartnerApplicationService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lấy hồ sơ quán đối tác của Manager (Web POS, nguồn dữ liệu <c>Cafe</c>). [Role: Manager]
        /// </summary>
        /// <response code="200">Trả về <see cref="ManagerCafeProfileResponseDto"/>.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có quán đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        [ProducesResponseType(typeof(ManagerCafeProfileResponseDto), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetProfile(CancellationToken cancellationToken = default)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.GetMyPartnerProfileAsync(managerId, cancellationToken);
            return NewResponse(200, ApiSuccessMessages.CafePartner.ProfileRetrieved, result);
        }

        /// <summary>
        /// Cập nhật hồ sơ vận hành (Giai đoạn 2) trước khi kích hoạt. [Role: Manager]
        /// </summary>
        /// <param name="request">Giờ mở cửa, hạ tầng, catalog game, sơ đồ bàn.</param>
        /// <param name="cancellationToken">Token huỷ request.</param>
        /// <response code="200">Cập nhật hồ sơ vận hành thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc quán đang ACTIVE (cần tạm dừng trước).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có quán đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPut("operational-profile")]
        [ProducesResponseType(typeof(ManagerCafeProfileResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> UpdateOperationalProfile(
            [FromBody] UpdateOperationalProfileRequestDto request,
            CancellationToken cancellationToken = default)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.UpdateOperationalProfileAsync(managerId, request, cancellationToken);
            return NewResponse(200, ApiSuccessMessages.CafePartner.OperationalProfileUpdated, result);
        }

        /// <summary>
        /// Kích hoạt quán (DATA_BLANK → ACTIVE) khi đủ điều kiện ràng buộc. [Role: Manager]
        /// </summary>
        /// <param name="cancellationToken">Token huỷ request.</param>
        /// <response code="200">Kích hoạt quán thành công, hiển thị trên Mobile App.</response>
        /// <response code="400">Chưa đủ điều kiện (≥5 bàn, ≥20 game, ≥3 ảnh, giờ mở cửa, sơ đồ bàn) hoặc trạng thái không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có quán đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("activate")]
        [ProducesResponseType(typeof(ManagerCafeProfileResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Activate(CancellationToken cancellationToken = default)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.ActivateAsync(managerId, cancellationToken);
            return NewResponse(200, ApiSuccessMessages.CafePartner.CafeActivated, result);
        }

        /// <summary>
        /// Tạm dừng hoạt động (ACTIVE → DATA_BLANK). [Role: Manager]
        /// </summary>
        /// <param name="cancellationToken">Token huỷ request.</param>
        /// <response code="200">Tạm dừng quán thành công, ẩn khỏi Mobile App.</response>
        /// <response code="400">Còn phiên đặt bàn đang chạy hoặc trạng thái không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có quán đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("deactivate")]
        [ProducesResponseType(typeof(ManagerCafeProfileResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Deactivate(CancellationToken cancellationToken = default)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.DeactivateAsync(managerId, cancellationToken);
            return NewResponse(200, ApiSuccessMessages.CafePartner.CafePaused, result);
        }

        /// <summary>
        /// Ngừng kinh doanh (ACTIVE/DATA_BLANK → INACTIVE). Có thể mở lại bằng <c>POST reopen</c>. [Role: Manager]
        /// </summary>
        /// <param name="cancellationToken">Token huỷ request.</param>
        /// <response code="200">Quán đã chuyển sang INACTIVE.</response>
        /// <response code="400">Còn phiên bàn đang chạy, quán đã INACTIVE/BANNED, hoặc trạng thái không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có quán đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("close")]
        [ProducesResponseType(typeof(ManagerCafeProfileResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> ClosePermanently(CancellationToken cancellationToken = default)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.ClosePermanentlyAsync(managerId, cancellationToken);
            return NewResponse(200, ApiSuccessMessages.CafePartner.CafeClosedPermanently, result);
        }

        /// <summary>
        /// Mở lại quán (INACTIVE → ACTIVE) khi đủ điều kiện ràng buộc. [Role: Manager]
        /// </summary>
        /// <param name="cancellationToken">Token huỷ request.</param>
        /// <response code="200">Mở lại quán thành công, hiển thị trên Mobile App.</response>
        /// <response code="400">Chưa đủ điều kiện kích hoạt, quán BANNED, hoặc trạng thái không phải INACTIVE.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có quyền Manager.</response>
        /// <response code="404">Chưa có quán đối tác đã được duyệt.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("reopen")]
        [ProducesResponseType(typeof(ManagerCafeProfileResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Reopen(CancellationToken cancellationToken = default)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.ReopenAsync(managerId, cancellationToken);
            return NewResponse(200, ApiSuccessMessages.CafePartner.CafeReopened, result);
        }

        /// <summary>
        /// Manager tự đặt trạng thái vận hành quán mình sở hữu — endpoint hợp nhất cho 3 trạng thái
        /// (<c>DATA_BLANK</c>, <c>ACTIVE</c>, <c>INACTIVE</c>). Trạng thái <c>BANNED</c> chỉ Admin
        /// mới có quyền đặt — dùng <c>PUT /api/v1/admin/cafes/{cafeId}/operational-status</c>.
        /// Khi chuyển sang <c>ACTIVE</c> áp dụng đầy đủ điều kiện kích hoạt; khi rời <c>ACTIVE</c>
        /// sang trạng thái khác yêu cầu không còn phiên bàn đang chạy. [Role: Manager]
        /// </summary>
        /// <param name="request">
        /// <c>Status</c>: <c>DATA_BLANK</c> | <c>ACTIVE</c> | <c>INACTIVE</c> (case-insensitive, trim, không chấp nhận <c>BANNED</c>).
        /// <c>Reason</c>: lý do chuyển trạng thái (chỉ dùng cho <c>INACTIVE</c>; gửi kèm cho target khác sẽ bị reject với 400).
        /// </param>
        /// <param name="cancellationToken">Token huỷ request.</param>
        /// <response code="200">Cập nhật trạng thái vận hành thành công, trả về hồ sơ quán sau khi đổi.</response>
        /// <response code="400">
        /// Status không hợp lệ, <c>reason</c> không phù hợp target, hoặc chuyển sang <c>ACTIVE</c> mà
        /// chưa đủ điều kiện (thiếu bàn/game/ảnh/giờ mở cửa/sơ đồ bàn/GPS).
        /// </response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Manager cố set <c>BANNED</c> (chỉ Admin có quyền).</response>
        /// <response code="404">Chưa có quán đối tác đã được duyệt.</response>
        /// <response code="409">Còn phiên bàn đang chạy khi rời <c>ACTIVE</c>.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPatch("operational-status")]
        [ProducesResponseType(typeof(ManagerCafeProfileResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> SetOperationalStatus(
            [FromBody] ManagerSetCafeOperationalStatusRequestDto request,
            CancellationToken cancellationToken = default)
        {
            var managerId = GetUserIdFromClaims();
            var result = await _service.ManagerSetOperationalStatusAsync(managerId, request, cancellationToken);
            return NewResponse(200, ApiSuccessMessages.CafePartner.OperationalStatusUpdated, result);
        }
    }
}
