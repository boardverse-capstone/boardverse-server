using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Controller cho Booking (đặt chỗ).
/// Tách riêng khỏi PaymentController để clear về mặt domain.
/// Flow: Lobby (Full) -> Booking -> BookingDeposit -> SePay -> Confirmed -> CheckIn -> Completed.
/// </summary>
[ApiController]
[Route("api/bookings")]
[Authorize]
[Produces("application/json")]
[Tags("Booking")]
public class BookingController : BaseApiController
{
    private readonly IBookingService _bookingService;

    public BookingController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    /// <summary>
    /// Tạo booking từ lobby đã lock.
    /// Host tạo booking sau khi lobby đã Full và đã khóa (lock).
    /// Booking ban đầu ở trạng thái PendingDeposit - chờ thanh toán cọc.
    /// [Role: Player — chỉ Host của lobby]
    /// </summary>
    /// <param name="request">Thông tin tạo booking (lobbyId, cafeId, bookingDate, startTime, endTime, totalSlot).</param>
    /// <response code="201">Tạo booking thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (giờ kết thúc trước giờ bắt đầu, ngày trong quá khứ).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Host của lobby.</response>
    /// <response code="404">Không tìm thấy lobby hoặc cafe.</response>
    /// <response code="409">Lobby chưa lock / đã có booking cho lobby này.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost]
    [ProducesResponseType(typeof(object), 201)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequestDto request)
    {
        var hostUserId = GetUserIdFromClaims();
        var result = await _bookingService.CreateBookingAsync(hostUserId, request);
        return NewResponse(201, "Tạo booking thành công.", result);
    }

    /// <summary>
    /// Lấy chi tiết booking theo ID.
    /// [Role: Player — chỉ owner; Manager, Admin — xem tất cả.]
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <response code="200">Lấy chi tiết booking thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền xem booking này.</response>
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(typeof(BookingResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetBooking(Guid bookingId)
    {
        var booking = await _bookingService.GetByIdAsync(bookingId);
        if (booking == null)
        {
            throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");
        }
        return NewResponse(200, "Lấy chi tiết booking thành công.", booking);
    }

    /// <summary>
    /// Lấy booking liên kết với lobby.
    /// [Role: Player — chỉ member của lobby; Manager, Admin.]
    /// </summary>
    /// <param name="lobbyId">Mã lobby.</param>
    /// <response code="200">Lấy booking thành công (null nếu chưa có booking).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="404">Không tìm thấy lobby.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("lobby/{lobbyId:guid}")]
    [ProducesResponseType(typeof(BookingResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetBookingByLobby(Guid lobbyId)
    {
        var booking = await _bookingService.GetByLobbyIdAsync(lobbyId);
        return NewResponse(200, "Lấy booking theo lobby thành công.", booking);
    }

    /// <summary>
    /// Lấy danh sách booking của user hiện tại.
    /// [Role: Player]
    /// </summary>
    /// <param name="status">Filter theo trạng thái (optional).</param>
    /// <response code="200">Lấy danh sách booking thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("my")]
    [ProducesResponseType(typeof(List<BookingResponseDto>), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetMyBookings([FromQuery] Core.Enum.BookingStatus? status = null)
    {
        var userId = GetUserIdFromClaims();
        var bookings = await _bookingService.GetByUserIdAsync(userId, userId);
        return NewResponse(200, "Lấy danh sách booking thành công.", bookings);
    }

    /// <summary>
    /// Lấy danh sách booking sắp tới của user hiện tại.
    /// [Role: Player]
    /// </summary>
    /// <param name="limit">Số lượng tối đa (1-50, default 10).</param>
    /// <response code="200">Lấy danh sách booking sắp tới thành công.</response>
    /// <response code="400">Limit không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("my/upcoming")]
    [ProducesResponseType(typeof(List<BookingResponseDto>), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetMyUpcomingBookings([FromQuery] int limit = 10)
    {
        if (limit < 1 || limit > 50)
        {
            throw new BadRequestException("Limit phải nằm trong khoảng 1-50.");
        }
        var userId = GetUserIdFromClaims();
        var bookings = await _bookingService.GetUpcomingByUserIdAsync(userId, limit);
        return NewResponse(200, "Lấy danh sách booking sắp tới thành công.", bookings);
    }

    /// <summary>
    /// Cập nhật booking (chỉ một số trường được phép: ngày, giờ, số ghế, ghi chú).
    /// Chỉ owner mới được sửa, và chỉ khi booking chưa check-in.
    /// [Role: Player — chỉ owner]
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <param name="request">Các trường muốn cập nhật.</param>
    /// <response code="200">Cập nhật booking thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải owner.</response>
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="409">Booking đã check-in/completed/cancelled.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPatch("{bookingId:guid}")]
    [ProducesResponseType(typeof(BookingResponseDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    public async Task<IActionResult> UpdateBooking(Guid bookingId, [FromBody] UpdateBookingRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _bookingService.UpdateBookingAsync(bookingId, userId, request);
        return NewResponse(200, "Cập nhật booking thành công.", result);
    }

    /// <summary>
    /// Hủy booking bởi user.
    /// Không thể hủy khi đã check-in hoặc completed.
    /// [Role: Player — chỉ owner]
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <param name="reason">Lý do hủy (optional).</param>
    /// <response code="200">Hủy booking thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải owner.</response>
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="409">Booking đã check-in/completed.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpDelete("{bookingId:guid}")]
    [ProducesResponseType(typeof(BookingResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    public async Task<IActionResult> CancelBooking(Guid bookingId, [FromQuery] string? reason = null)
    {
        var userId = GetUserIdFromClaims();
        var result = await _bookingService.CancelBookingAsync(bookingId, userId, reason);
        return NewResponse(200, "Hủy booking thành công.", result);
    }

    /// <summary>
    /// Check-in tại quán.
    /// Chỉ booking ở trạng thái Confirmed mới check-in được.
    /// [Role: Manager, CafeStaff]
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <response code="200">Check-in thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền (không phải Manager/Staff của cafe).</response>
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="409">Booking không ở trạng thái Confirmed.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{bookingId:guid}/check-in")]
    [Authorize(Roles = "Manager,CafeStaff")]
    [ProducesResponseType(typeof(BookingResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    public async Task<IActionResult> CheckIn(Guid bookingId)
    {
        var staffUserId = GetUserIdFromClaims();
        var result = await _bookingService.CheckInAsync(bookingId, staffUserId);
        return NewResponse(200, "Check-in thành công.", result);
    }

    /// <summary>
    /// Check-out tại quán.
    /// Chỉ booking đã check-in mới check-out được.
    /// [Role: Manager, CafeStaff]
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <response code="200">Check-out thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="409">Booking chưa check-in.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{bookingId:guid}/check-out")]
    [Authorize(Roles = "Manager,CafeStaff")]
    [ProducesResponseType(typeof(BookingResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    public async Task<IActionResult> CheckOut(Guid bookingId)
    {
        var staffUserId = GetUserIdFromClaims();
        var result = await _bookingService.CheckOutAsync(bookingId, staffUserId);
        return NewResponse(200, "Check-out thành công.", result);
    }
}
