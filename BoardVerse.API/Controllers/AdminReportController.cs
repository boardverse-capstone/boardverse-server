using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin endpoints cho báo cáo: overview statistics, lobby-failures, deposits, cafe-performance.
/// [Role: Admin]
/// </summary>
[ApiController]
[Route("api/v1/admin/reports")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
[Tags("Admin - Reports")]
public class AdminReportController : BaseApiController
{
    private readonly IAdminReportService _reportService;

    public AdminReportController(IAdminReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// Lấy tổng quan dashboard: users, cafes, tournaments, lobbies, bookings, deposits, revenue.
    /// [Role: Admin]
    /// </summary>
    /// <response code="200">Tổng quan dashboard.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(AdminDashboardOverviewDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetOverview()
    {
        var result = await _reportService.GetDashboardOverviewAsync();
        return NewResponse(200, "Tổng quan dashboard.", result);
    }

    /// <summary>
    /// Báo cáo lobby failures: tổng hợp theo loại (timeout, host-cancelled, cafe-rejected, cafe-expired).
    /// [Role: Admin]
    /// </summary>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 20, max 100).</param>
    /// <param name="fromUtc">Bắt đầu khoảng thời gian (UTC).</param>
    /// <param name="toUtc">Kết thúc khoảng thời gian (UTC).</param>
    /// <param name="failureType">Filter theo loại: TimeoutFailed, HostCancelled, RejectedByCafe, ExpiredByCafe.</param>
    /// <response code="200">Báo cáo lobby failures.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("lobby-failures")]
    [ProducesResponseType(typeof(AdminLobbyFailuresReportDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetLobbyFailuresReport(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string? failureType = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var result = await _reportService.GetLobbyFailuresReportAsync(
            page, pageSize, fromUtc, toUtc, failureType);
        return NewResponse(200, "Báo cáo lobby failures.", result);
    }

    /// <summary>
    /// Báo cáo deposits: tổng hợp theo trạng thái (pending, paid, refunded, forfeited).
    /// [Role: Admin]
    /// </summary>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 20, max 100).</param>
    /// <param name="fromUtc">Bắt đầu khoảng thời gian (UTC).</param>
    /// <param name="toUtc">Kết thúc khoảng thời gian (UTC).</param>
    /// <response code="200">Báo cáo deposits.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("deposits")]
    [ProducesResponseType(typeof(AdminDepositsReportDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetDepositsReport(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var result = await _reportService.GetDepositsReportAsync(page, pageSize, fromUtc, toUtc);
        return NewResponse(200, "Báo cáo deposits.", result);
    }

    /// <summary>
    /// Báo cáo performance của tất cả cafes: bookings, lobbies, tournaments, revenue.
    /// [Role: Admin]
    /// </summary>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 20, max 100).</param>
    /// <param name="sortBy">Sắp xếp theo: totalBookings, completionRate, failureRate, totalRevenue (mặc định totalRevenue).</param>
    /// <param name="sortDescending">Sắp xếp giảm dần (mặc định true).</param>
    /// <response code="200">Báo cáo cafe performance.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("cafe-performance")]
    [ProducesResponseType(typeof(AdminCafePerformanceReportDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetCafePerformanceReport(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "totalRevenue",
        [FromQuery] bool sortDescending = true)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var result = await _reportService.GetCafePerformanceReportAsync(
            page, pageSize, sortBy, sortDescending);
        return NewResponse(200, "Báo cáo cafe performance.", result);
    }

    /// <summary>
    /// Báo cáo chi tiết performance của một cafe cụ thể.
    /// [Role: Admin]
    /// </summary>
    /// <param name="cafeId">Mã cafe.</param>
    /// <param name="fromUtc">Bắt đầu khoảng thời gian (UTC).</param>
    /// <param name="toUtc">Kết thúc khoảng thời gian (UTC).</param>
    /// <response code="200">Báo cáo chi tiết cafe.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("cafe-performance/{cafeId:guid}")]
    [ProducesResponseType(typeof(AdminCafePerformanceDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetCafePerformanceDetail(
        Guid cafeId,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null)
    {
        var result = await _reportService.GetCafePerformanceDetailAsync(cafeId, fromUtc, toUtc);
        if (result == null)
        {
            return NotFound(new { message = $"Không tìm thấy cafe '{cafeId}'." });
        }
        return NewResponse(200, "Báo cáo chi tiết cafe.", result);
    }
}
