using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Quản lý lịch cafe (override ngày cụ thể).
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
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
    /// Lấy toàn bộ lịch override của cafe. [Role: Cafe Manager.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <response code="200">Lấy lịch cafe thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ cafe.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    [HttpGet]
    public async Task<IActionResult> GetSchedule(Guid cafeId)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _cafeScheduleService.GetScheduleAsync(cafeId);
        return this.NewResponse(200, ApiSuccessMessages.CafeSchedule.ScheduleRetrieved, data);
    }

    /// <summary>
    /// Tạo hoặc cập nhật override cho 1 ngày. [Role: Cafe Manager.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="request">ApplyDate, OpenTime, CloseTime, IsClosed.</param>
    /// <response code="200">Cập nhật override thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ cafe.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
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
    /// Xóa override cho 1 ngày. [Role: Cafe Manager.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="applyDate">Ngày cần xóa override.</param>
    /// <response code="204">Xóa override thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ cafe.</response>
    /// <response code="404">Không tìm thấy cafe hoặc override.</response>
    [HttpDelete("{applyDate}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteOverride(Guid cafeId, DateOnly applyDate)
    {
        var managerUserId = GetUserIdFromClaims();
        await _cafeScheduleService.DeleteOverrideAsync(cafeId, managerUserId, applyDate);
        return NoContent();
    }
}
