using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Controller cho các API voting/rating sau khi check-in/check-out (mobile gap #4, #5).
/// Tất cả endpoint đều yêu cầu user là member của lobby liên kết với booking.
/// </summary>
[ApiController]
[Route("api/bookings/{bookingId:guid}")]
[Authorize]
[Produces("application/json")]
[Tags("Booking Rating")]
public class BookingRatingController : BaseApiController
{
    private readonly IBookingRatingService _bookingRatingService;

    public BookingRatingController(IBookingRatingService bookingRatingService)
    {
        _bookingRatingService = bookingRatingService;
    }

    /// <summary>
    /// Gửi/cập nhật phiếu vote vắng mặt cho booking (mobile gap #4).
    /// BR: Booking phải CheckedIn, voter là lobby member active, không vote chính mình.
    /// Idempotent: vote lần 2 sẽ UPDATE vote trước.
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <param name="request">Danh sách thành viên bị vote vắng mặt.</param>
    /// <response code="200">Vote thành công, trả về thống kê vote hiện tại.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải lobby member hoặc vote chính mình.</response>
    /// <response code="404">Không tìm thấy booking/lobby.</response>
    /// <response code="409">Booking không ở CheckedIn / quá thời hạn vote.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("no-show-votes")]
    [ProducesResponseType(typeof(NoShowVoteResponseDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    public async Task<IActionResult> SubmitNoShowVote(
        Guid bookingId,
        [FromBody] SubmitNoShowVoteRequestDto request)
    {
        var voterId = GetUserIdFromClaims();
        var result = await _bookingRatingService.SubmitNoShowVoteAsync(bookingId, voterId, request);
        return NewResponse(200, "Gửi phiếu vote vắng mặt thành công.", result);
    }

    /// <summary>
    /// Gửi lượt chấm điểm chéo cho booking (mobile gap #5 - POST ratings).
    /// BR: Voter là lobby member, không rate chính mình, mỗi ratedUser chỉ 1 lần.
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <param name="request">Danh sách ratings (attitude/sportsmanship/punctuality + comment).</param>
    /// <response code="200">Chấm điểm thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải lobby member.</response>
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("ratings")]
    [ProducesResponseType(typeof(BookingRatingResponseDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> SubmitRatings(
        Guid bookingId,
        [FromBody] SubmitBookingRatingsRequestDto request)
    {
        var voterId = GetUserIdFromClaims();
        var result = await _bookingRatingService.SubmitRatingsAsync(bookingId, voterId, request);
        return NewResponse(200, "Gửi chấm điểm thành công.", result);
    }

    /// <summary>
    /// Lấy trạng thái rating của voter cho booking (mobile gap #5 - GET status).
    /// Mobile dùng để ẩn/hiện form chấm điểm.
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <response code="200">Trả về trạng thái rating.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải lobby member.</response>
    /// <response code="404">Không tìm thấy booking/lobby.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("ratings/status")]
    [ProducesResponseType(typeof(BookingRatingStatusDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetRatingStatus(Guid bookingId)
    {
        var voterId = GetUserIdFromClaims();
        var result = await _bookingRatingService.GetRatingStatusAsync(bookingId, voterId);
        return NewResponse(200, "Lấy trạng thái chấm điểm thành công.", result);
    }
}