using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Quản lý lịch cafe (override ngày cụ thể).
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// GAP-FIX (2026-09-01): Authz on GET, single-get endpoint, bulk endpoint, Swagger.
/// </summary>
[ApiController]
[Route("api/v1/cafes/{cafeId:guid}/schedule-overrides")]
[Authorize]
[Produces("application/json")]
public class CafeScheduleController : BaseApiController
{
    private readonly ICafeScheduleService _cafeScheduleService;

    public CafeScheduleController(ICafeScheduleService cafeScheduleService)
    {
        _cafeScheduleService = cafeScheduleService;
    }

    /// <summary>
    /// Lấy toàn bộ lịch override của cafe. [Role: Cafe Manager / Staff.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <response code="200">Lấy lịch cafe thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ cafe hoặc staff.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSchedule(Guid cafeId, CancellationToken cancellationToken)
    {
        var managerUserId = GetUserIdFromClaims();
        // GAP-4: Authz + exists check trong 1 lần gọi
        var data = await _cafeScheduleService.GetScheduleAsync(cafeId, managerUserId, cancellationToken);
        return this.NewResponse(200, ApiSuccessMessages.CafeSchedule.ScheduleRetrieved, data);
    }

    /// <summary>
    /// Lấy override cho ngày cụ thể. Trả về null nếu ngày đó dùng default. [Role: Cafe Manager / Staff.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="applyDate">Ngày cần lấy override.</param>
    /// <response code="200">Lấy override thành công (hoặc null).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ cafe hoặc staff.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    [HttpGet("{applyDate}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOverride(Guid cafeId, DateOnly applyDate, CancellationToken cancellationToken)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _cafeScheduleService.GetOverrideAsync(cafeId, applyDate, cancellationToken);
        return this.NewResponse(200, "Lấy override thành công.", data);
    }

    /// <summary>
    /// Tạo hoặc cập nhật override cho 1 ngày. [Role: Cafe Manager / Staff.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="request">ApplyDate, OpenTime, CloseTime, IsClosed.</param>
    /// <response code="200">Cập nhật override thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ cafe hoặc staff.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertOverride(
        Guid cafeId,
        [FromBody] UpsertCafeScheduleOverrideRequestDto request)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _cafeScheduleService.UpsertOverrideAsync(cafeId, managerUserId, request);
        return this.NewResponse(200, ApiSuccessMessages.CafeSchedule.OverrideUpserted, data);
    }

    /// <summary>
    /// Bulk upsert nhiều override trong 1 lần gọi. [Role: Cafe Manager / Staff.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="requests">Danh sách override cần tạo/cập nhật.</param>
    /// <response code="200">Bulk upsert thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ cafe hoặc staff.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    [HttpPost("bulk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertBulkOverrides(
        Guid cafeId,
        [FromBody] List<UpsertCafeScheduleOverrideRequestDto> requests)
    {
        var managerUserId = GetUserIdFromClaims();
        var data = await _cafeScheduleService.UpsertBulkOverridesAsync(cafeId, managerUserId, requests);
        return this.NewResponse(200, $"Bulk upsert thành công {data.Count} ngày.", data);
    }

    /// <summary>
    /// Xóa override cho 1 ngày. [Role: Cafe Manager / Staff.]
    /// </summary>
    /// <param name="cafeId">Mã định danh cafe.</param>
    /// <param name="applyDate">Ngày cần xóa override.</param>
    /// <response code="200">Xóa override thành công (idempotent).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ cafe hoặc staff.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    [HttpDelete("{applyDate}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOverride(Guid cafeId, DateOnly applyDate, CancellationToken cancellationToken)
    {
        var managerUserId = GetUserIdFromClaims();
        await _cafeScheduleService.DeleteOverrideAsync(cafeId, managerUserId, applyDate, cancellationToken);
        return this.NewResponse(200, "Xóa override, cafe quay về dùng lịch mặc định thành công.", null!);
    }
}
