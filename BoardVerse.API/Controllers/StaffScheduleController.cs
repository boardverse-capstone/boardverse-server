using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Controller quản lý lịch làm việc cho staff (Manager) và staff self-service (CafeStaff).
/// Bao gồm: lịch làm việc, copy template tuần, yêu cầu nghỉ phép, đổi ca, điểm danh check-in/out, ngày nghỉ.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class StaffScheduleController : BaseApiController
{
    private readonly IStaffScheduleService _scheduleService;
    private readonly ITimeOffRequestService _timeOffService;
    private readonly IShiftSwapRequestService _shiftSwapService;
    private readonly IShiftAttendanceService _attendanceService;
    private readonly IStaffUnavailableDateService _unavailableService;
    private readonly ICafeRepository _cafeRepository;

    public StaffScheduleController(
        IStaffScheduleService scheduleService,
        ITimeOffRequestService timeOffService,
        IShiftSwapRequestService shiftSwapService,
        IShiftAttendanceService attendanceService,
        IStaffUnavailableDateService unavailableService,
        ICafeRepository cafeRepository)
    {
        _scheduleService = scheduleService;
        _timeOffService = timeOffService;
        _shiftSwapService = shiftSwapService;
        _attendanceService = attendanceService;
        _unavailableService = unavailableService;
        _cafeRepository = cafeRepository;
    }

    /// <summary>
    /// Đảm bảo caller là Manager (chủ quán) của cafe, hoặc Admin. Trả về true nếu pass.
    /// Endpoint Manager-only nào cũng nên gọi method này đầu tiên để chặn IDOR.
    /// </summary>
    private async Task EnsureManagerOwnsCafeAsync(Guid cafeId, CancellationToken cancellationToken)
    {
        // Admin bypass
        if (User.IsInRole("Admin")) return;

        var userId = GetUserIdFromClaims();
        var cafe = await _cafeRepository.GetByIdAsync(cafeId, cancellationToken);
        if (cafe == null || cafe.ManagerId != userId)
            throw new BoardVerse.Core.Exceptions.ForbiddenException(ApiErrorMessages.StaffSchedule.NotCafeManagerOf(cafeId));
    }

    #region Manager: Staff Schedules

    /// <summary>
    /// Lấy danh sách lịch làm việc của quán. [Role: Manager, Admin]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="staffUserId">Lọc theo staff (optional).</param>
    /// <param name="dayOfWeek">Lọc theo thứ trong tuần (optional, 0=Sunday, 1=Monday, ...).</param>
    /// <response code="200">Trả về danh sách lịch làm việc.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("v1/cafes/{cafeId:guid}/staff-schedules")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetStaffSchedules(
        Guid cafeId,
        [FromQuery] Guid? staffUserId,
        [FromQuery] DayOfWeek? dayOfWeek)
    {
        var result = await _scheduleService.GetByCafeAsync(cafeId, staffUserId, dayOfWeek);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.ScheduleListRetrieved, result);
    }

    /// <summary>
    /// Tạo mới 1 lịch làm việc cho staff. [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="dto">Thông tin lịch làm việc (staff, thứ, giờ bắt đầu/kết thúc, loại ca, ...).</param>
    /// <response code="201">Tạo lịch thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (giờ trùng nhau, không thuộc quán).</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="404">Không tìm thấy quán.</response>
    /// <response code="409">Lịch bị trùng với ca khác trong cùng khung giờ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("v1/cafes/{cafeId:guid}/staff-schedules")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> CreateStaffSchedule(
        Guid cafeId,
        [FromBody] CreateStaffScheduleRequestDto dto)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var result = await _scheduleService.CreateAsync(cafeId, dto);
        return this.NewResponse(201, ApiSuccessMessages.StaffSchedule.ScheduleCreated, result);
    }

    /// <summary>
    /// Tạo nhiều lịch làm việc cùng lúc (bulk). [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="dto">Danh sách lịch cần tạo.</param>
    /// <response code="201">Tạo lịch hàng loạt thành công.</response>
    /// <response code="400">Danh sách lịch rỗng hoặc dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("v1/cafes/{cafeId:guid}/staff-schedules/bulk")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> BulkCreateStaffSchedules(
        Guid cafeId,
        [FromBody] BulkCreateStaffScheduleRequestDto dto)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var result = await _scheduleService.BulkCreateAsync(cafeId, dto);
        return this.NewResponse(201, ApiSuccessMessages.StaffSchedule.ScheduleCreated, result);
    }

    /// <summary>
    /// Cập nhật lịch làm việc. [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="scheduleId">Mã lịch làm việc.</param>
    /// <param name="dto">Thông tin cập nhật.</param>
    /// <response code="200">Cập nhật thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="404">Không tìm thấy lịch làm việc.</response>
    /// <response code="409">Lịch bị trùng với ca khác trong cùng khung giờ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPut("v1/cafes/{cafeId:guid}/staff-schedules/{scheduleId:guid}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> UpdateStaffSchedule(
        Guid cafeId,
        Guid scheduleId,
        [FromBody] UpdateStaffScheduleRequestDto dto)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var result = await _scheduleService.UpdateAsync(cafeId, scheduleId, dto);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.ScheduleUpdated, result);
    }

    /// <summary>
    /// Xóa lịch làm việc. [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="scheduleId">Mã lịch làm việc.</param>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="404">Không tìm thấy lịch làm việc.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete("v1/cafes/{cafeId:guid}/staff-schedules/{scheduleId:guid}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeleteStaffSchedule(Guid cafeId, Guid scheduleId)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        await _scheduleService.DeleteAsync(cafeId, scheduleId);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.ScheduleDeleted, new { id = scheduleId });
    }

    /// <summary>
    /// Copy lịch làm việc từ tuần nguồn sang tuần đích. [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="dto">Tuần nguồn, tuần đích, optional filter theo staff.</param>
    /// <response code="200">Copy thành công.</response>
    /// <response code="400">Tuần đích phải sau tuần nguồn.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("v1/cafes/{cafeId:guid}/staff-schedules/copy-template")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> CopyWeekTemplate(
        Guid cafeId,
        [FromBody] CopyWeekTemplateRequestDto dto)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var result = await _scheduleService.CopyWeekTemplateAsync(cafeId, dto);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.TemplatesCopied, result);
    }

    /// <summary>
    /// Tổng hợp số giờ làm của staff trong khoảng ngày. [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="staffUserId">Mã nhân viên.</param>
    /// <param name="startDate">Ngày bắt đầu (yyyy-MM-dd).</param>
    /// <param name="endDate">Ngày kết thúc (yyyy-MM-dd).</param>
    /// <response code="200">Trả về tổng hợp giờ làm.</response>
    /// <response code="400">Ngày kết thúc phải sau ngày bắt đầu.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("v1/cafes/{cafeId:guid}/staff-schedules/hours-summary")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetWorkHoursSummary(
        Guid cafeId,
        [FromQuery] Guid staffUserId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var result = await _scheduleService.GetWorkHoursSummaryAsync(staffUserId, cafeId, startDate, endDate);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.WorkHoursRetrieved, result);
    }

    #endregion

    #region Manager: Time-off requests

    /// <summary>
    /// Danh sách yêu cầu nghỉ phép của quán. [Role: Manager, Admin]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="status">Lọc theo trạng thái (Pending/Approved/Rejected/Cancelled).</param>
    /// <response code="200">Trả về danh sách yêu cầu nghỉ phép.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("v1/cafes/{cafeId:guid}/time-off-requests")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetTimeOffRequests(Guid cafeId, [FromQuery] TimeOffStatus? status)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var result = await _timeOffService.GetByCafeAsync(cafeId, status);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.TimeOffRequestsRetrieved, result);
    }

    /// <summary>
    /// Duyệt / từ chối yêu cầu nghỉ phép. [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="id">Mã yêu cầu nghỉ phép.</param>
    /// <param name="dto">Trạng thái duyệt (Approved/Rejected) + ghi chú.</param>
    /// <response code="200">Xử lý yêu cầu thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="404">Không tìm thấy yêu cầu.</response>
    /// <response code="409">Yêu cầu đã được xử lý trước đó.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("v1/cafes/{cafeId:guid}/time-off-requests/{id:guid}/review")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ReviewTimeOffRequest(
        Guid cafeId,
        Guid id,
        [FromBody] ReviewTimeOffRequestDto dto)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var userId = GetUserIdFromClaims();
        var result = await _timeOffService.ReviewAsync(id, userId, dto);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.TimeOffRequestReviewed, result);
    }

    #endregion

    #region Manager: Shift swaps

    /// <summary>
    /// Danh sách yêu cầu đổi ca của quán. [Role: Manager, Admin]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="status">Lọc theo trạng thái.</param>
    /// <response code="200">Trả về danh sách yêu cầu đổi ca.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("v1/cafes/{cafeId:guid}/shift-swaps")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> GetShiftSwaps(Guid cafeId, [FromQuery] ShiftSwapStatus? status)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var result = await _shiftSwapService.GetByCafeAsync(cafeId, status);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.SwapRequestsRetrieved, result);
    }

    /// <summary>
    /// Duyệt / từ chối yêu cầu đổi ca. [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="id">Mã yêu cầu đổi ca.</param>
    /// <param name="dto">Trạng thái duyệt + ghi chú.</param>
    /// <response code="200">Xử lý yêu cầu thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền quản lý quán này.</response>
    /// <response code="404">Không tìm thấy yêu cầu.</response>
    /// <response code="409">Yêu cầu đã được xử lý trước đó.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("v1/cafes/{cafeId:guid}/shift-swaps/{id:guid}/review")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ReviewShiftSwap(
        Guid cafeId,
        Guid id,
        [FromBody] ReviewShiftSwapRequestDto dto)
    {
        await EnsureManagerOwnsCafeAsync(cafeId, HttpContext.RequestAborted);
        var userId = GetUserIdFromClaims();
        var result = await _shiftSwapService.ReviewAsync(id, userId, dto);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.SwapRequestReviewed, result);
    }

    #endregion

    #region Staff self-service

    /// <summary>
    /// Lấy lịch làm việc của tôi tại 1 quán. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <response code="200">Trả về danh sách lịch của tôi.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("staff/my-schedules/{cafeId:guid}")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> GetMySchedules(Guid cafeId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _scheduleService.GetMyScheduleAsync(userId, cafeId);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.ScheduleRetrieved, result);
    }

    /// <summary>
    /// Lấy lịch làm việc của tôi trong khoảng ngày. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="startDate">Ngày bắt đầu (yyyy-MM-dd).</param>
    /// <param name="endDate">Ngày kết thúc (yyyy-MM-dd).</param>
    /// <response code="200">Trả về danh sách lịch trong khoảng ngày.</response>
    /// <response code="400">Ngày kết thúc phải sau ngày bắt đầu.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("staff/my-schedules/{cafeId:guid}/range")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> GetMyScheduleRange(
        Guid cafeId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var userId = GetUserIdFromClaims();
        var result = await _scheduleService.GetMyScheduleForDateRangeAsync(userId, cafeId, startDate, endDate);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.ScheduleRetrieved, result);
    }

    /// <summary>
    /// Lấy danh sách điểm danh của tôi trong khoảng ngày. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="startDate">Ngày bắt đầu.</param>
    /// <param name="endDate">Ngày kết thúc.</param>
    /// <response code="200">Trả về danh sách điểm danh.</response>
    /// <response code="400">Ngày kết thúc phải sau ngày bắt đầu.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("staff/my-attendance/{cafeId:guid}")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> GetMyAttendance(
        Guid cafeId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var userId = GetUserIdFromClaims();
        var result = await _attendanceService.GetByStaffAsync(userId, cafeId, startDate, endDate);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.AttendanceRetrieved, result);
    }

    /// <summary>
    /// Check-in ca làm việc. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán (chỉ để rõ route, không dùng trong xử lý).</param>
    /// <param name="scheduleId">Mã lịch làm việc (từ my-schedules).</param>
    /// <param name="dto">Giờ check-in và ghi chú.</param>
    /// <response code="201">Check-in thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền điểm danh cho lịch này.</response>
    /// <response code="404">Không tìm thấy lịch làm việc.</response>
    /// <response code="409">Bạn đã check-in ca này rồi.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("staff/my-attendance/{cafeId:guid}/check-in")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> CheckIn(
        Guid cafeId,
        [FromQuery] Guid scheduleId,
        [FromBody] CheckInAttendanceDto dto)
    {
        _ = cafeId;
        var userId = GetUserIdFromClaims();
        var result = await _attendanceService.CheckInAsync(scheduleId, userId, dto);
        return this.NewResponse(201, ApiSuccessMessages.StaffSchedule.CheckedIn, result);
    }

    /// <summary>
    /// Check-out ca làm việc. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="id">Mã bản ghi điểm danh (từ my-attendance).</param>
    /// <param name="dto">Giờ check-out và ghi chú.</param>
    /// <response code="200">Check-out thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không có quyền check-out bản ghi này.</response>
    /// <response code="404">Không tìm thấy bản ghi điểm danh.</response>
    /// <response code="409">Bạn cần check-in trước, hoặc đã check-out rồi.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("staff/my-attendance/{id:guid}/check-out")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> CheckOut(
        Guid id,
        [FromBody] CheckOutAttendanceDto dto)
    {
        var userId = GetUserIdFromClaims();
        var result = await _attendanceService.CheckOutAsync(id, userId, dto);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.CheckedOut, result);
    }

    /// <summary>
    /// Danh sách yêu cầu nghỉ phép của tôi. [Role: CafeStaff, Manager]
    /// </summary>
    /// <response code="200">Trả về danh sách yêu cầu nghỉ phép của tôi.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("staff/my-time-off")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> GetMyTimeOff()
    {
        var userId = GetUserIdFromClaims();
        var result = await _timeOffService.GetMyRequestsAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.TimeOffRequestsRetrieved, result);
    }

    /// <summary>
    /// Tạo yêu cầu nghỉ phép của tôi. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="dto">Ngày bắt đầu, kết thúc, lý do.</param>
    /// <response code="201">Tạo yêu cầu thành công.</response>
    /// <response code="400">Ngày kết thúc phải sau ngày bắt đầu, hoặc không thuộc quán.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("staff/my-time-off/{cafeId:guid}")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> CreateMyTimeOff(
        Guid cafeId,
        [FromBody] CreateTimeOffRequestDto dto)
    {
        var userId = GetUserIdFromClaims();
        var result = await _timeOffService.CreateAsync(cafeId, userId, dto);
        return this.NewResponse(201, ApiSuccessMessages.StaffSchedule.TimeOffRequestCreated, result);
    }

    /// <summary>
    /// Danh sách ngày nghỉ cố định của tôi. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="from">Ngày bắt đầu (optional).</param>
    /// <param name="to">Ngày kết thúc (optional).</param>
    /// <response code="200">Trả về danh sách ngày nghỉ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("staff/my-unavailable/{cafeId:guid}")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> GetMyUnavailable(
        Guid cafeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var userId = GetUserIdFromClaims();
        var result = await _unavailableService.GetMyUnavailableDatesAsync(userId, cafeId, from, to);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.UnavailableDatesRetrieved, result);
    }

    /// <summary>
    /// Thêm ngày nghỉ cố định cho tôi. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="dto">Ngày cụ thể + lý do.</param>
    /// <response code="201">Thêm ngày nghỉ thành công.</response>
    /// <response code="400">Không thuộc quán.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="409">Ngày này đã được đánh dấu nghỉ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("staff/my-unavailable/{cafeId:guid}")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> CreateMyUnavailable(
        Guid cafeId,
        [FromBody] CreateUnavailableDateDto dto)
    {
        var userId = GetUserIdFromClaims();
        var result = await _unavailableService.CreateAsync(cafeId, userId, dto);
        return this.NewResponse(201, ApiSuccessMessages.StaffSchedule.UnavailableDateCreated, result);
    }

    /// <summary>
    /// Xóa ngày nghỉ cố định của tôi. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="id">Mã ngày nghỉ.</param>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy ngày nghỉ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete("staff/my-unavailable/{id:guid}")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> DeleteMyUnavailable(Guid id)
    {
        var userId = GetUserIdFromClaims();
        await _unavailableService.DeleteAsync(id, userId);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.UnavailableDateDeleted, new { id });
    }

    /// <summary>
    /// Danh sách yêu cầu đổi ca tôi đã gửi. [Role: CafeStaff, Manager]
    /// </summary>
    /// <response code="200">Trả về danh sách yêu cầu đổi ca đã gửi.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("staff/shift-swaps/my-requests")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> GetMyShiftSwapRequests()
    {
        var userId = GetUserIdFromClaims();
        var result = await _shiftSwapService.GetByRequesterAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.SwapRequestsRetrieved, result);
    }

    /// <summary>
    /// Danh sách yêu cầu đổi ca gửi đến tôi (cần tôi đồng ý / Manager duyệt). [Role: CafeStaff, Manager]
    /// </summary>
    /// <response code="200">Trả về danh sách yêu cầu đổi ca nhận được.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("staff/shift-swaps/incoming")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> GetIncomingShiftSwaps()
    {
        var userId = GetUserIdFromClaims();
        var result = await _shiftSwapService.GetIncomingForStaffAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.StaffSchedule.SwapRequestsRetrieved, result);
    }

    /// <summary>
    /// Tạo yêu cầu đổi ca. [Role: CafeStaff, Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán.</param>
    /// <param name="dto">Thông tin đổi ca: target staff + ca của tôi + ca muốn nhận.</param>
    /// <response code="201">Tạo yêu cầu đổi ca thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy ca.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("staff/shift-swaps/{cafeId:guid}")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> CreateShiftSwap(
        Guid cafeId,
        [FromBody] CreateShiftSwapRequestDto dto)
    {
        var userId = GetUserIdFromClaims();
        var result = await _shiftSwapService.CreateAsync(cafeId, userId, dto);
        return this.NewResponse(201, ApiSuccessMessages.StaffSchedule.SwapRequestCreated, result);
    }

    #endregion
}