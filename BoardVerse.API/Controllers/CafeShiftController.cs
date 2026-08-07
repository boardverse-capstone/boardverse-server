using BoardVerse.Core.DTOs.CafeShift;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

[ApiController]
[Route("api/shifts")]
[Authorize]
public class CafeShiftController : BaseApiController
{
    private readonly ICafeShiftService _shiftService;

    public CafeShiftController(ICafeShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    /// <summary>
    /// Mở ca làm việc mới cho quán. [Role: Manager, CafeStaff]
    /// </summary>
    /// <param name="dto">Thông tin mở ca (CafeId, OpeningCashBalance).</param>
    /// <response code="201">Mở ca thành công, trả về thông tin ca.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền vận hành quán này.</response>
    /// <response code="404">Không tìm thấy quán.</response>
    /// <response code="409">Đã có ca đang mở. Cần đóng ca hiện tại trước.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost]
    public async Task<IActionResult> OpenShift([FromBody] OpenShiftRequestDto dto)
    {
        var userId = GetUserIdFromClaims();
        var result = await _shiftService.OpenShiftAsync(dto.CafeId, userId, dto.OpeningCashBalance);
        return this.NewResponse(201, ApiSuccessMessages.CafeShift.ShiftOpened, result);
    }

    /// <summary>
    /// Đóng ca làm việc đang mở. [Role: Manager, CafeStaff]
    /// </summary>
    /// <param name="shiftId">Mã định danh ca làm việc.</param>
    /// <param name="dto">Thông tin đóng ca (ClosingCashBalance).</param>
    /// <response code="200">Đóng ca thành công, trả về thông tin ca sau khi đóng.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền vận hành quán này.</response>
    /// <response code="404">Không tìm thấy ca làm việc.</response>
    /// <response code="409">Ca đã được đóng trước đó.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{shiftId:guid}/close")]
    public async Task<IActionResult> CloseShift(Guid shiftId, [FromBody] CloseShiftRequestDto dto)
    {
        var userId = GetUserIdFromClaims();
        var result = await _shiftService.CloseShiftAsync(shiftId, userId, dto.ClosingCashBalance);
        return this.NewResponse(200, ApiSuccessMessages.CafeShift.ShiftClosed, result);
    }

    /// <summary>
    /// Lấy ca đang mở của quán. [Role: Manager, CafeStaff]
    /// </summary>
    /// <param name="cafeId">Mã định danh quán.</param>
    /// <response code="200">Trả về ca đang mở hoặc null nếu không có ca nào.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền truy cập quán này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentShift([FromQuery] Guid cafeId)
    {
        var result = await _shiftService.GetCurrentShiftAsync(cafeId);
        return this.NewResponse(200, ApiSuccessMessages.CafeShift.ShiftOpened, result);
    }

    /// <summary>
    /// Lấy lịch sử các ca làm việc của quán (phân trang). [Role: Manager, CafeStaff]
    /// </summary>
    /// <param name="cafeId">Mã định danh quán.</param>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 10, tối đa 100).</param>
    /// <response code="200">Trả về danh sách ca làm việc phân trang.</response>
    /// <response code="400">Tham số phân trang không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền truy cập quán này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("history")]
    public async Task<IActionResult> GetShiftHistory(
        [FromQuery] Guid cafeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var result = await _shiftService.GetShiftHistoryAsync(cafeId, page, pageSize);
        return this.NewResponse(200, ApiSuccessMessages.CafeShift.ShiftHistoryRetrieved, result);
    }
}
