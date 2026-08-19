using BoardVerse.Core.DTOs.WalkIn;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// **FLOW C — WALK-IN (POS only)**.
/// Walk-in window + walk-in booking cho khách vãng lai không qua lobby.
///
/// Route prefix: /api/v1/reservations/walkin
/// Tất cả endpoints yêu cầu quyền POS staff.
///
/// §4.4 + §10.3 + Flow C.
/// </summary>
[ApiController]
[Route("api/v1/reservations/walkin")]
[Authorize]
public class WalkInController : BaseApiController
{
    private readonly IWalkInService _walkInService;

    public WalkInController(IWalkInService walkInService)
    {
        _walkInService = walkInService;
    }

    /// <summary>
    /// Lấy danh sách WalkInWindow đang trống của 1 cafe + ngày.
    /// POS staff gọi trước khi tạo walk-in.
    /// [Role: Staff / Manager / Admin]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="date">Ngày muốn xem (yyyy-MM-dd).</param>
    /// <response code="200">Danh sách WalkInWindow trống.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có quyền POS.</response>
    [HttpGet("windows")]
    [ProducesResponseType(typeof(WalkInWindowsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWalkInWindows(
        [FromQuery] Guid cafeId,
        [FromQuery] DateOnly date,
        CancellationToken ct)
    {
        var result = await _walkInService.GetWalkInWindowsAsync(cafeId, date, ct);
        return Ok(result);
    }

    /// <summary>
    /// Tạo WalkInBooking cho khách vãng lai.
    /// BR-WALKIN-01: Chỉ tạo walk-in khi WalkInWindow.Status ∈ {Available, Partial}.
    /// BR-WALKIN-05: OCC trên WalkInWindow.Version (EC-06).
    /// BR-WALKIN-04: Walk-in KHÔNG cọc — thanh toán 100% tại POS.
    /// [Role: Staff / Manager / Admin]
    /// </summary>
    /// <param name="request">Thông tin walk-in booking.</param>
    /// <response code="201">WalkInBooking tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có quyền POS.</response>
    /// <response code="404">WalkInWindow không tìm thấy.</response>
    /// <response code="409">Window không còn khả dụng / không đủ ghế / race condition.</response>
    [HttpPost]
    [ProducesResponseType(typeof(WalkInBookingResponseDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWalkInBooking(
        [FromBody] CreateWalkInBookingRequestDto request,
        CancellationToken ct)
    {
        // TODO: Get actual POS staff ID from JWT claims
        var posStaffId = GetUserIdFromClaims();

        var result = await _walkInService.CreateWalkInBookingAsync(request, posStaffId, ct);
        return CreatedAtAction(nameof(CreateWalkInBooking), new { id = result.Id }, result);
    }

    /// <summary>
    /// Đóng WalkInWindow thủ công (bởi POS staff).
    /// [Role: Staff / Manager / Admin]
    /// </summary>
    /// <param name="windowId">Mã WalkInWindow cần đóng.</param>
    /// <param name="request">Lý do đóng (optional).</param>
    /// <response code="200">Đóng thành công.</response>
    /// <response code="404">WalkInWindow không tìm thấy.</response>
    [HttpPost("windows/{windowId:guid}/close")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CloseWalkInWindow(
        Guid windowId,
        [FromBody] CloseWalkInWindowRequestDto? request,
        CancellationToken ct)
    {
        await _walkInService.CloseWindowAsync(windowId, request?.Reason, ct);
        return Ok(new { message = "WalkInWindow đã được đóng." });
    }

    /// <summary>
    /// Hủy WalkInBooking (chỉ khi chưa check-in).
    /// Trả ghế về WalkInWindow.
    /// §10.3: POST /api/v1/reservations/walkin/{id}/cancel
    /// [Role: Staff / Manager / Admin]
    /// </summary>
    /// <param name="walkInBookingId">Mã WalkInBooking cần hủy.</param>
    /// <response code="200">Hủy thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có quyền POS.</response>
    /// <response code="404">WalkInBooking không tìm thấy.</response>
    /// <response code="409">Conflict: đã check-in rồi (không thể hủy).</response>
    [HttpPost("{walkInBookingId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelWalkInBooking(
        Guid walkInBookingId,
        CancellationToken ct)
    {
        try
        {
            await _walkInService.CancelWalkInBookingAsync(walkInBookingId, ct);
            return Ok(new { message = "WalkInBooking đã được hủy." });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
