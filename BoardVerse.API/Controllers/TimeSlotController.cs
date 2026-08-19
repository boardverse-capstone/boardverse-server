using BoardVerse.Core.DTOs.TimeSlotOverride;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Quản lý <c>TimeSlot</c> cho manager.
/// BR-NEW-15 §7.1: 4 time slot cố định (Morning / Afternoon / Evening / LateNight),
/// không thể thêm slot mới. Manager chỉ override StartTime / EndTime / IsClosed cho từng cafe qua <c>CafeScheduleOverride</c>.
/// </summary>
[ApiController]
[Route("api/v1/manager/time-slots")]
[Authorize(Roles = "Manager")]
public class TimeSlotController : BaseApiController
{
    private readonly ITimeSlotService _timeSlotService;

    public TimeSlotController(ITimeSlotService timeSlotService)
    {
        _timeSlotService = timeSlotService;
    }

    /// <summary>
    /// Lấy 4 khung giờ mặc định của hệ thống (metadata read-only). [Role: Manager]
    /// </summary>
    /// <response code="200">Trả về 4 TimeSlot mặc định: Morning, Afternoon, Evening, LateNight.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không có role Manager.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("defaults")]
    [ProducesResponseType(typeof(IReadOnlyList<DefaultTimeSlotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDefaultTimeSlots()
    {
        var data = await _timeSlotService.GetDefaultTimeSlotsAsync();
        return NewResponse(200, ApiSuccessMessages.TimeSlot.DefaultSlotsRetrieved, data);
    }

    /// <summary>
    /// Lấy toàn bộ 4 khung giờ của cafe (đã merge với override nếu có). [Role: Manager — chỉ chủ cafe.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <response code="200">Danh sách 4 TimeSlot kèm thông tin override/default.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không phải Manager hoặc không sở hữu cafe này.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("cafes/{cafeId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<ManagerTimeSlotResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCafeTimeSlots(Guid cafeId)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _timeSlotService.GetCafeTimeSlotsAsync(cafeId, managerUserId);
        return NewResponse(200, ApiSuccessMessages.TimeSlot.CafeSlotsRetrieved, data);
    }

    /// <summary>
    /// Lấy chi tiết 1 khung giờ của cafe (override hoặc default). [Role: Manager — chỉ chủ cafe.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="timeSlot">Enum TimeSlot: <c>morning</c> / <c>afternoon</c> / <c>evening</c> / <c>lateNight</c>.</param>
    /// <response code="200">Chi tiết TimeSlot của cafe.</response>
    /// <response code="400">timeSlot không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không sở hữu cafe này.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("cafes/{cafeId:guid}/{timeSlot:regex(^(Morning|Afternoon|Evening|LateNight)$)}")]
    [ProducesResponseType(typeof(ManagerTimeSlotResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCafeTimeSlot(Guid cafeId, string timeSlot)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _timeSlotService.GetCafeTimeSlotAsync(cafeId, managerUserId, timeSlot);
        return NewResponse(200, ApiSuccessMessages.TimeSlot.SlotRetrieved, data);
    }

    /// <summary>
    /// Tạo override cho 1 khung giờ của cafe. Nếu đã tồn tại → 409 Conflict (dùng PUT để update). [Role: Manager — chỉ chủ cafe.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="request">timeSlot, startTime, endTime, isClosed, effectiveFrom, effectiveTo.</param>
    /// <response code="201">Override đã tạo.</response>
    /// <response code="400">timeSlot không hợp lệ, startTime == endTime (khi !isClosed), effectiveFrom &gt; effectiveTo.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không sở hữu cafe này.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    /// <response code="409">Cafe đã có override cho slot này — dùng PUT để cập nhật.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("cafes/{cafeId:guid}")]
    [ProducesResponseType(typeof(ManagerTimeSlotResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateOverride(
        Guid cafeId,
        [FromBody] CreateTimeSlotOverrideRequestDto request)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _timeSlotService.CreateOverrideAsync(cafeId, managerUserId, request);
        return NewResponse(201, ApiSuccessMessages.TimeSlot.OverrideCreated, data);
    }

    /// <summary>
    /// Cập nhật (partial) override cho 1 khung giờ của cafe. Field null = giữ nguyên giá trị hiện tại. [Role: Manager — chỉ chủ cafe.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="timeSlot">Enum TimeSlot: <c>morning</c> / <c>afternoon</c> / <c>evening</c> / <c>lateNight</c>.</param>
    /// <param name="request">Các field cần đổi (StartTime, EndTime, IsClosed, EffectiveFrom, EffectiveTo). Field null = giữ nguyên.</param>
    /// <response code="200">Override đã cập nhật.</response>
    /// <response code="400">timeSlot không hợp lệ, không có field nào để update, hoặc giá trị mới không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không sở hữu cafe này.</response>
    /// <response code="404">Cafe chưa có override cho slot này — dùng POST để tạo.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPut("cafes/{cafeId:guid}/{timeSlot:regex(^(Morning|Afternoon|Evening|LateNight)$)}")]
    [ProducesResponseType(typeof(ManagerTimeSlotResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateOverride(
        Guid cafeId,
        string timeSlot,
        [FromBody] UpdateTimeSlotOverrideRequestDto request)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _timeSlotService.UpdateOverrideAsync(cafeId, managerUserId, timeSlot, request);
        return NewResponse(200, ApiSuccessMessages.TimeSlot.OverrideUpdated, data);
    }

    /// <summary>
    /// Xóa override cho 1 khung giờ của cafe → quay về dùng lịch mặc định. Idempotent. [Role: Manager — chỉ chủ cafe.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="timeSlot">Enum TimeSlot: <c>morning</c> / <c>afternoon</c> / <c>evening</c> / <c>lateNight</c>.</param>
    /// <response code="204">Override đã xóa (kể cả khi chưa có).</response>
    /// <response code="400">timeSlot không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không sở hữu cafe này.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete("cafes/{cafeId:guid}/{timeSlot:regex(^(Morning|Afternoon|Evening|LateNight)$)}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteOverride(Guid cafeId, string timeSlot)
    {
        var managerUserId = GetUserIdFromClaims();
        await _timeSlotService.DeleteOverrideAsync(cafeId, managerUserId, timeSlot);
        return NoContent();
    }
}
