using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Controller cho các API booking dành cho Player mobile (booking-payment-gaps.md #1, #2).
/// Read-only: liệt kê bàn trống + khảo sát capacity quán trong khung giờ cụ thể.
/// </summary>
[ApiController]
[Route("api/cafes/{cafeId:guid}")]
[Authorize]
[Produces("application/json")]
[Tags("Cafe Booking")]
public class CafeBookingController : BaseApiController
{
    private readonly ICafeBookingService _cafeBookingService;

    public CafeBookingController(ICafeBookingService cafeBookingService)
    {
        _cafeBookingService = cafeBookingService;
    }

    /// <summary>
    /// Lấy danh sách bàn trống phù hợp với khung giờ + số ghế yêu cầu.
    /// Mobile BookingSummaryPage dùng để hiển thị dropdown chọn bàn trước khi gọi POST /api/bookings.
    /// [Role: Player đã đăng nhập.]
    /// </summary>
    /// <param name="cafeId">Mã quán cafe.</param>
    /// <param name="scheduledStartTime">Giờ bắt đầu dự kiến (ISO 8601 UTC).</param>
    /// <param name="scheduleEndTime">Giờ kết thúc dự kiến (ISO 8601 UTC).</param>
    /// <param name="seatCount">Số ghế tối thiểu (>=1).</param>
    /// <response code="200">Danh sách bàn trống (có thể rỗng).</response>
    /// <response code="400">Thiếu query param hoặc giờ không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="404">Không tìm thấy quán.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("available-tables")]
    public async Task<IActionResult> GetAvailableTables(
        Guid cafeId,
        [FromQuery] DateTime scheduledStartTime,
        [FromQuery] DateTime scheduleEndTime,
        [FromQuery] int seatCount)
    {
        var result = await _cafeBookingService.GetAvailableTablesAsync(
            cafeId, scheduledStartTime, scheduleEndTime, seatCount);
        return NewResponse(200, "Lấy danh sách bàn trống thành công.", result);
    }

    /// <summary>
    /// Khảo sát capacity của quán trong khung giờ cụ thể + đề xuất slot thay thế.
    /// Mobile BoardGameDetailPage dùng để cảnh báo "hết chỗ" trước khi user vào luồng booking.
    /// [Role: Player đã đăng nhập.]
    /// </summary>
    /// <param name="cafeId">Mã quán cafe.</param>
    /// <param name="startTime">Giờ bắt đầu muốn đặt (ISO 8601 UTC).</param>
    /// <param name="endTime">Giờ kết thúc muốn đặt (ISO 8601 UTC).</param>
    /// <param name="seatCount">Số ghế player cần (default 1).</param>
    /// <param name="gameTemplateId">Optional - game đang chọn để check box có sẵn không.</param>
    /// <response code="200">Trả về thông tin capacity + slot thay thế.</response>
    /// <response code="400">Thiếu query param hoặc giờ không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="404">Không tìm thấy quán.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(
        Guid cafeId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime,
        [FromQuery] int seatCount = 1,
        [FromQuery] Guid? gameTemplateId = null)
    {
        var result = await _cafeBookingService.GetAvailabilityAsync(
            cafeId, startTime, endTime, seatCount, gameTemplateId);
        return NewResponse(200, "Khảo sát capacity quán thành công.", result);
    }
}