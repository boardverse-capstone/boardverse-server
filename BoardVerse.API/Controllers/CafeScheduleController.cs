using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Quản lý lịch cafe (override slot time / đóng slot).
/// BR-NEW-15: 4 time slot cố định, cafe override start/end hoặc IsClosed.
/// </summary>
[ApiController]
[Route("api/v1/cafes/{cafeId:guid}/schedule-overrides")]
[Authorize]
public class CafeScheduleController : BaseApiController
{
    private readonly ICafeScheduleService _cafeScheduleService;

    public CafeScheduleController(ICafeScheduleService cafeScheduleService)
    {
        _cafeScheduleService = cafeScheduleService;
    }

    /// <summary>
    /// Lấy toàn bộ lịch (4 slot) của cafe, kèm override nếu có. [Role: Cafe Manager — chỉ chủ cafe.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <response code="200">Lấy lịch cafe thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ cafe.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet]
    public async Task<IActionResult> GetSchedule(Guid cafeId)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _cafeScheduleService.GetScheduleAsync(cafeId);
        return this.NewResponse(200, ApiSuccessMessages.CafeSchedule.ScheduleRetrieved, data);
    }

    /// <summary>
    /// Tạo hoặc cập nhật override cho 1 slot. [Role: Cafe Manager — chỉ chủ cafe.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="request">TimeSlot, StartTime, EndTime, IsClosed, EffectiveFrom/To.</param>
    /// <response code="200">Cập nhật override thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (vd: StartTime == EndTime khi mở slot).</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ cafe.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost]
    public async Task<IActionResult> UpsertOverride(
        Guid cafeId,
        [FromBody] UpsertCafeScheduleOverrideRequestDto request)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _cafeScheduleService.UpsertOverrideAsync(cafeId, managerUserId, request);
        return this.NewResponse(200, ApiSuccessMessages.CafeSchedule.OverrideUpserted, data);
    }

    /// <summary>
    /// Xóa override cho 1 slot → cafe quay về dùng lịch mặc định. [Role: Cafe Manager — chỉ chủ cafe.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="timeSlot">Slot cần xóa override (morning/afternoon/evening/night).</param>
    /// <response code="204">Xóa override thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ cafe.</response>
    /// <response code="404">Không tìm thấy cafe hoặc override.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete("{timeSlot}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteOverride(Guid cafeId, TimeSlot timeSlot)
    {
        var managerUserId = GetUserIdFromClaims();
        await _cafeScheduleService.DeleteOverrideAsync(cafeId, managerUserId, timeSlot);
        return NoContent();
    }
}
