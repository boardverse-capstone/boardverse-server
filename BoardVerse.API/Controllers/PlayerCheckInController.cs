using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    /// <summary>
    /// API để player quét QR do POS tạo, tự check-in vào reservation của mình.
    /// Hỗ trợ chiều thứ 2 của luồng check-in 2 chiều (BR §21A.7): thay vì staff scan
    /// QR player, player scan QR POS. Hữu ích cho demo và khi player cầm điện thoại dễ scan hơn.
    /// </summary>
    [ApiController]
    [Route("api/check-in")]
    [Authorize]
    public class PlayerCheckInController : BaseApiController
    {
        private readonly IPlayerCheckInService _playerCheckInService;

        public PlayerCheckInController(IPlayerCheckInService playerCheckInService)
        {
            _playerCheckInService = playerCheckInService;
        }

        /// <summary>
        /// Player scan QR code hiển thị trên POS → gửi token cho backend.
        /// Backend lookup token trong DB, xác minh token còn hiệu lực, xác minh player là thành viên của
        /// reservation liên kết, tự động chọn bàn + hộp game còn trống, gọi nội bộ logic check-in
        /// hiện có của POS và trả về thông tin ActiveSession vừa khởi tạo.
        ///
        /// Token chỉ dùng được 1 lần (consumed). Mỗi lần scan trả về 200 nếu thành công hoặc 4xx với
        /// lý do cụ thể (hết hạn, đã dùng, đã thu hồi, không phải thành viên reservation...).
        /// </summary>
        /// <param name="request">Payload chứa 16-char token in hoa, alphanumeric (không bao gồm 0/1/I/O).</param>
        /// <response code="200">Check-in thành công, trả về thông tin ActiveSession cho player.</response>
        /// <response code="400">Token không đúng định dạng (16-char alphanumeric uppercase, loại trừ 0/1/I/O).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Player không phải host hoặc thành viên active của reservation liên kết với QR này.</response>
        /// <response code="404">Không tìm thấy token hoặc reservation liên kết không tồn tại.</response>
        /// <response code="409">Token đã được sử dụng trước đó (consumed).</response>
        /// <response code="410">Token đã hết hạn TTL hoặc đã bị thu hồi (revoked).</response>
        /// <response code="422">Reservation không trong khung giờ check-in hoặc quán không còn bàn/hộp game trống.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("scan-qr")]
        public async Task<IActionResult> ScanQr([FromBody] PlayerScanTokenRequestDto request)
        {
            var (userId, _) = GetOptionalViewerContext();
            if (userId == null)
            {
                return Unauthorized();
            }
            var result = await _playerCheckInService.CheckInByTokenAsync(userId.Value, request);
            return this.NewResponse(200, ApiSuccessMessages.Pos.PlayerCheckedInByToken, result);
        }
    }
}
