using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Player-facing APIs để quản lý phiên chơi của mình.
/// Cho phép player xem phiên hiện tại, gia hạn thời gian, và thanh toán bằng BVC.
/// [Role: Player — yêu cầu đăng nhập]
/// </summary>
[ApiController]
[Route("api/v1/sessions")]
[Authorize]
[Produces("application/json")]
[Tags("PlayerSession")]
public class PlayerSessionController : BaseApiController
{
    private readonly IActiveSessionService _activeSessionService;

    public PlayerSessionController(IActiveSessionService activeSessionService)
    {
        _activeSessionService = activeSessionService;
    }

    /// <summary>
    /// Lấy thông tin phiên chơi hiện tại của player đang đăng nhập.
    /// Bao gồm thời gian đã chơi, ước tính chi phí, và trạng thái.
    /// [Role: Player]
    /// </summary>
    /// <response code="200">Trả về thông tin phiên hiện tại.</response>
    /// <response code="404">Không có phiên nào đang hoạt động.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("me/current")]
    [ProducesResponseType(typeof(GetCurrentSessionResponseDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetCurrentSession()
    {
        var userId = GetUserIdFromClaims();
        var session = await _activeSessionService.GetCurrentSessionAsync(userId);
        return NewResponse(200, "Lấy thông tin phiên chơi thành công.", session);
    }

/// <summary>
/// Gửi yêu cầu gia hạn thêm thời gian chơi.
/// Lưu ý: Việc gia hạn thực tế cần được xác nhận bởi nhân viên tại quán.
/// [Role: Player]
/// </summary>
/// <param name="request">Số phút muốn gia hạn thêm.</param>
/// <response code="200">Yêu cầu gia hạn đã được gửi.</response>
/// <response code="400">Số phút gia hạn không hợp lệ.</response>
/// <response code="409">Không thể gia hạn ở trạng thái hiện tại.</response>
/// <response code="404">Không có phiên nào đang hoạt động.</response>
/// <response code="500">Lỗi hệ thống không mong đợi.</response>
[HttpPost("me/extend")]
[ProducesResponseType(typeof(ExtendSessionResponseDto), 200)]
[ProducesResponseType(400)]
[ProducesResponseType(409)]
[ProducesResponseType(404)]
[ProducesResponseType(500)]
public async Task<IActionResult> ExtendSession([FromBody] ExtendSessionRequestDto request)
{
    // GAP-R4-A3 Fix: Để ApiExceptionMiddleware xử lý exception thay vì catch thủ công.
    // Đảm bảo response shape đồng nhất với các endpoint khác.
    var userId = GetUserIdFromClaims();
    var result = await _activeSessionService.ExtendSessionAsync(userId, request.ExtensionMinutes);
    return NewResponse(200, result.Message ?? "Gia hạn thành công.", result);
}

    /// <summary>
    /// Thanh toán hóa đơn phiên chơi bằng BVC.
    /// Chỉ hoạt động khi phiên đang ở trạng thái Unpaid.
    /// [Role: Player]
    /// </summary>
/// <param name="request">Thông tin thanh toán (sessionId).</param>
/// <response code="200">Thanh toán thành công.</response>
/// <response code="400">Yêu cầu không hợp lệ.</response>
/// <response code="403">Bạn không tham gia phiên này.</response>
/// <response code="409">Phiên không ở trạng thái thanh toán.</response>
/// <response code="429">Vượt quá giới hạn thanh toán (10 lần / 5 phút).</response>
/// <response code="500">Lỗi hệ thống không mong đợi.</response>
[HttpPost("me/pay")]
[EnableRateLimiting("PaymentPolicy")]
[ProducesResponseType(typeof(PlayerPaySessionResponseDto), 200)]
[ProducesResponseType(400)]
[ProducesResponseType(403)]
[ProducesResponseType(409)]
[ProducesResponseType(429)]
[ProducesResponseType(500)]
public async Task<IActionResult> PaySession([FromBody] PlayerPaySessionRequestDto request)
{
    // GAP-R4-A3 Fix: Để ApiExceptionMiddleware xử lý exception + trả ApiResponse đồng nhất.
    // ApiExceptionMiddleware đã map InsufficientBvcBalanceException → 402 với data shape
    // { code, message, data: { currentBalance, requiredBalance, missingAmount, action } }.
    // Trước đây PaySession catch inline trả raw anonymous object → FE phải parse 2 shape.
    var userId = GetUserIdFromClaims();
    var result = await _activeSessionService.PlayerPaySessionAsync(userId, request.SessionId);
    return NewResponse(200, result.Message ?? "Thanh toán thành công.", result);
}

    /// <summary>
    /// Lấy lịch sử các phiên đã chơi của player.
    /// GAP-8 + GAP-2 + GAP-7 Fix: Trả danh sách phiên đã paid (bao gồm walk-in) + cursor pagination + date range.
    /// </summary>
    /// <param name="limit">Số lượng phiên tối đa trả về (mặc định 20, tối đa 100).</param>
    /// <param name="beforePaidAt">Cursor: lấy phiên cũ hơn mốc thời gian này (UTC, optional, cho load-more).</param>
    /// <param name="fromDate">Lọc session từ ngày này trở đi (UTC, optional).</param>
    /// <param name="toDate">Lọc session đến ngày này (UTC, optional).</param>
    /// <response code="200">Trả về danh sách lịch sử phiên.</response>
    /// <response code="400">Limit không hợp lệ hoặc fromDate > toDate.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("me/history")]
    [ProducesResponseType(typeof(List<SessionHistoryResponseDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetSessionHistory(
        [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery] DateTime? beforePaidAt = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
        {
            return BadRequest("fromDate phải nhỏ hơn hoặc bằng toDate.");
        }

        var userId = GetUserIdFromClaims();
        var history = await _activeSessionService.GetSessionHistoryAsync(userId, limit, beforePaidAt, fromDate, toDate);
        return NewResponse(200, "Lấy lịch sử phiên thành công.", history);
    }
}
