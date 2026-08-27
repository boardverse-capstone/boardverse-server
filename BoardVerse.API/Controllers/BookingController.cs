using BoardVerse.API.Filters;
using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Controller cho Booking (đặt chỗ).
/// **FLOW B — BOOKING (CŨ, SePay per-member deposit + walk-in + time-slot + extension).**
/// KHÔNG dùng IReservationRepository.
/// Flow: Lobby (Full) -> Booking (PendingDeposit) -> BookingDeposit -> SePay -> Confirmed -> CheckedIn -> NoShow/Cancelled.
///
/// ## Deprecation notice (Phase 1 + Phase 4 — RFC 8594)
///
/// **Booking flow (Flow B) đang được migrate sang Reservation flow (Flow A).**
/// - **POST /api/bookings** — chỉ dùng cho legacy flow (BR-22, SePay per-member deposit). **Khuyến nghị dùng `/api/v1/reservations/confirm`** thay thế.
/// - **GET /api/bookings/{id}** — có thể dùng cho admin/audit. Reservation đã confirmed có `ReservationCode` 8-char — tương đương với `Booking.VerificationCode`.
/// - **DELETE /api/bookings/{id}** — legacy cancellation. **Dùng `/api/v1/reservations/{id}/cancel`** thay thế.
/// - Các endpoint khác — ít dùng, kiểm tra FE trước khi xóa.
///
/// Sunset date 2026-12-31 — sau ngày này, controller sẽ đổi sang trả 410 Gone.
/// </summary>
[ApiController]
[Route("api/bookings")]
[Authorize]
[Produces("application/json")]
[Tags("Booking")]
[LegacyBookingGate]
[DeprecationHeaders(
    Sunset = "Wed, 31 Dec 2026 23:59:59 GMT",
    DocsLink = "/docs/api/booking#deprecation")]
#pragma warning disable CS0618 // §13.1 Phase 1: Controller deprecated — removed in Phase 4
[Obsolete("§13.1 Phase 1: BookingController đang bị deprecate. Dùng /api/v1/reservations/* thay thế. Xóa ở Phase 4 khi FE xác nhận không còn sử dụng.")]
public class BookingController : BaseApiController
#pragma warning restore CS0618
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
    /// <param name="request">Thông tin tạo booking (lobbyId, cafeId, cafeTableId, scheduledStartTime, scheduleEndTime).</param>
    /// <response code="201">Tạo booking thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (giờ kết thúc trước giờ bắt đầu, bàn không thuộc cafe, trùng giờ).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Host của lobby.</response>
    /// <response code="404">Không tìm thấy lobby, cafe hoặc bàn.</response>
    /// <response code="409">Lobby chưa lock / đã có booking cho lobby này / bàn trùng giờ.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BookingResponseDto), 201)]
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
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(typeof(BookingResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetBooking(Guid bookingId)
    {
        var userId = GetUserIdFromClaims();
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
        var booking = await _bookingService.GetByIdForCallerAsync(bookingId, userId, role);
        if (booking == null)
        {
            throw new NotFoundException(ApiErrorMessages.Booking.BookingNotFoundById(bookingId));
        }
        return NewResponse(200, "Lấy chi tiết booking thành công.", booking);
    }

    /// <summary>
    /// Lấy realtime session status cho booking — chỉ member của lobby hoặc deposit owner (walk-in).
    /// Mobile task #8: trả về ActiveSession + members + estimated bill để hiển thị cho member khi Staff partial-checkout.
    /// [Role: Player — chỉ member lobby đã check-in; Manager/Admin.]
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <response code="200">Trả về ActiveSession status và danh sách member (kèm partial bill nếu có).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải member của booking.</response>
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("{bookingId:guid}/session-status")]
    [ProducesResponseType(typeof(BookingSessionStatusResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetSessionStatus(Guid bookingId)
    {
        var userId = GetUserIdFromClaims();
        var status = await _bookingService.GetSessionStatusAsync(bookingId, userId);
        return NewResponse(200, "Lấy session status thành công.", status);
    }

    /// <summary>
    /// Lấy booking liên kết với lobby.
    /// [Role: Player — chỉ member của lobby; Manager, Admin.]
    /// </summary>
    /// <param name="lobbyId">Mã lobby.</param>
    /// <response code="200">Booking (null nếu chưa có).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("lobby/{lobbyId:guid}")]
    [ProducesResponseType(typeof(BookingResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetBookingByLobby(Guid lobbyId)
    {
        var userId = GetUserIdFromClaims();
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
        var booking = await _bookingService.GetByLobbyIdForCallerAsync(lobbyId, userId, role);
        return NewResponse(200, "Lấy booking theo lobby thành công.", booking);
    }

    /// <summary>
    /// Lấy danh sách booking của cafe (theo ngày).
    /// Manager/CafeStaff/Admin: xem full BookingResponseDto.
    /// Player: xem BookingCafeSummaryDto (rút gọn, không lộ QR/paymentRef/memberIds) — task #14.
    /// </summary>
    /// <param name="cafeId">Mã cafe.</param>
    /// <response code="200">Lấy danh sách booking thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("cafe/{cafeId:guid}")]
    [ProducesResponseType(typeof(List<BookingResponseDto>), 200)]
    [ProducesResponseType(typeof(List<BookingCafeSummaryDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 404)]
    public async Task<IActionResult> GetBookingsByCafe(Guid cafeId)
    {
        var userId = GetUserIdFromClaims();
        var isStaffOrManager = User.IsInRole("Manager") || User.IsInRole("CafeStaff") || User.IsInRole("Admin");

        // GAP-C1: IDOR fix — players only see bookings they participate in.
        // Staff/Manager of THIS cafe get full data. Admin gets full data.
        var bookings = await _bookingService.GetByCafeIdAsync(cafeId, userId, isStaffOrManager);

        if (isStaffOrManager)
        {
            return NewResponse(200, "Lấy danh sách booking của quán thành công.", bookings);
        }

        // Player view: rút gọn — không lộ verificationQRCode/paymentRef/memberIds
        var summary = bookings.Select(b => new BookingCafeSummaryDto
        {
            Id = b.Id,
            ScheduledStartTime = b.ScheduledStartTime,
            ScheduleEndTime = b.ScheduleEndTime,
            PlayerQuantity = b.PlayerQuantity,
            Status = b.StatusText ?? b.Status.ToString()
        }).ToList();

        return NewResponse(200, "Lấy danh sách booking của quán thành công.", summary);
    }

    /// <summary>
    /// Cập nhật booking (bàn, thời gian, số người).
    /// Chỉ owner mới được sửa, và chỉ khi booking chưa check-in và chưa Cancelled.
    /// [Role: Player — chỉ owner (Host lobby)]
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <param name="request">Các trường muốn cập nhật.</param>
    /// <response code="200">Cập nhật booking thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải owner.</response>
    /// <response code="404">Không tìm thấy booking hoặc bàn.</response>
    /// <response code="409">Booking đã check-in/cancelled.</response>
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
    /// Không thể hủy khi đã check-in.
    /// [Role: Player — chỉ owner (Host lobby)]
    /// </summary>
    /// <param name="bookingId">Mã booking.</param>
    /// <param name="reason">Lý do hủy (optional).</param>
    /// <response code="200">Hủy booking thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải owner.</response>
    /// <response code="404">Không tìm thấy booking.</response>
    /// <response code="409">Booking đã check-in.</response>
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

    // Removed legacy endpoints (BR §XXI-B.1, BR §21A.7):
    // - `POST /api/bookings/{bookingId}/check-in`  → dùng `POST /api/cafes/{cafeId}/pos/check-in`
    // - `POST /api/bookings/{bookingId}/check-out` → `ReservationService.CompleteAndCaptureAsync` khi ActiveSession PAID.
}
