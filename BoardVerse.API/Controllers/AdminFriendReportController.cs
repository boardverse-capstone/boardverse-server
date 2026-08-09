using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin: Quản lý friend reports do người chơi gửi về vi phạm của bạn bè.
/// BR-FRIEND-REPORT: Player gửi report (qua FriendController); Admin xem/xử lý qua controller này.
/// </summary>
[ApiController]
[Route("api/v1/admin/friend-reports")]
[Authorize(Roles = "Admin")]
public class AdminFriendReportController : BaseApiController
{
    private readonly IFriendReportService _friendReportService;

    public AdminFriendReportController(IFriendReportService friendReportService)
    {
        _friendReportService = friendReportService;
    }

    /// <summary>
    /// Lấy danh sách friend reports với filter theo status + phân trang.
    /// Mặc định status=Pending để admin ưu tiên xử lý các report chưa review. [Role: Admin]
    /// </summary>
    /// <param name="status">Lọc theo trạng thái: Pending / Reviewed / Dismissed (optional).</param>
    /// <param name="offset">Bỏ qua N records đầu (default 0).</param>
    /// <param name="limit">Số records trả về (1-100, default 50).</param>
    /// <response code="200">Danh sách friend reports kèm tổng count.</response>
    /// <response code="400">Status filter không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status = "Pending",
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50)
    {
        var safeOffset = Math.Max(0, offset);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var (items, total) = await _friendReportService.GetAllForAdminAsync(status, safeOffset, safeLimit);
        return this.NewResponse(200, "Lấy danh sách friend report thành công.", new
        {
            items,
            total,
            offset = safeOffset,
            limit = safeLimit
        });
    }

    /// <summary>
    /// Admin xử lý friend report: đánh dấu Reviewed (đã xử lý) hoặc Dismissed (bỏ qua).
    /// Bắt buộc nhập adminNote để audit. [Role: Admin]
    /// </summary>
    /// <param name="reportId">Mã friend report.</param>
    /// <param name="request">Status mới (Reviewed/Dismissed) + AdminNote (bắt buộc).</param>
    /// <response code="200">Đã xử lý report thành công.</response>
    /// <response code="400">Status hoặc AdminNote không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không có quyền Admin.</response>
    /// <response code="404">Không tìm thấy report.</response>
    /// <response code="409">Report đã được xử lý trước đó.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{reportId:guid}/resolve")]
    public async Task<IActionResult> Resolve(
        Guid reportId,
        [FromBody] ResolveFriendReportRequestDto request)
    {
        var adminId = GetUserIdFromClaims();
        var result = await _friendReportService.ResolveAsync(adminId, reportId, request.Status, request.AdminNote);
        return this.NewResponse(200, "Đã xử lý friend report.", result);
    }
}

/// <summary>Request body cho AdminFriendReportController.Resolve.</summary>
public class ResolveFriendReportRequestDto
{
    /// <summary>Reviewed (đã xử lý) hoặc Dismissed (bỏ qua).</summary>
    public string Status { get; set; } = "Reviewed";

    /// <summary>Ghi chú admin (bắt buộc, dùng để audit).</summary>
    public string AdminNote { get; set; } = string.Empty;
}