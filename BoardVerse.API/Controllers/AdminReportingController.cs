using BoardVerse.Core.DTOs.Reports;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin endpoints cho Payment Breakdown Reporting + Feature Flag Metrics (M2 Phase 6 / Task C5.3, C5.5).
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C5.
/// [Role: Admin, Manager — Manager có thể xem report cafe của mình.]
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin,Manager")]
[Produces("application/json")]
[Tags("Admin - Reporting")]
public class AdminReportingController : BaseApiController
{
    private readonly IPaymentBreakdownReportService _reportService;
    private readonly IFeatureFlagService _featureFlagService;

    public AdminReportingController(
        IPaymentBreakdownReportService reportService,
        IFeatureFlagService featureFlagService)
    {
        _reportService = reportService;
        _featureFlagService = featureFlagService;
    }

    /// <summary>
    /// Lấy payment breakdown per cafe per month cho khoảng thời gian yêu cầu.
    /// Query trực tiếp từ MemberPaymentAuditLogs + MemberDepositAuditLogs + MemberPayments + ActiveSessions.
    /// [Role: Admin, Manager]
    /// </summary>
    /// <param name="cafeId">Optional. Filter theo cafe cụ thể. Null = tất cả cafe.</param>
    /// <param name="fromMonth">Tháng bắt đầu (yyyy-MM). Default = tháng hiện tại.</param>
    /// <param name="toMonth">Tháng kết thúc (yyyy-MM). Default = tháng hiện tại.</param>
    /// <response code="200">Báo cáo breakdown thành công.</response>
    /// <response code="400">Query param không hợp lệ (định dạng tháng sai).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin hoặc Manager.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("reports/payment-breakdown")]
    [ProducesResponseType(typeof(PaymentBreakdownReportDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetPaymentBreakdownReport(
        [FromQuery] Guid? cafeId = null,
        [FromQuery] string? fromMonth = null,
        [FromQuery] string? toMonth = null)
    {
        DateTime? fromParsed = null;
        DateTime? toParsed = null;

        if (!string.IsNullOrWhiteSpace(fromMonth))
        {
            if (!TryParseYearMonth(fromMonth, out var parsed))
            {
                return NewResponse(400,
                    $"Tham số fromMonth '{fromMonth}' không hợp lệ. Định dạng yyyy-MM.",
                    null);
            }
            fromParsed = parsed;
        }

        if (!string.IsNullOrWhiteSpace(toMonth))
        {
            if (!TryParseYearMonth(toMonth, out var parsed))
            {
                return NewResponse(400,
                    $"Tham số toMonth '{toMonth}' không hợp lệ. Định dạng yyyy-MM.",
                    null);
            }
            toParsed = parsed;
        }

        var report = await _reportService.GenerateReportAsync(cafeId, fromParsed, toParsed);
        return NewResponse(200, "Báo cáo payment breakdown theo cafe × tháng.", report);
    }

    /// <summary>
    /// Lấy snapshot metrics counters + gauges + active feature flags hiện tại.
    /// Metrics được track in-memory bởi <see cref="IFeatureFlagService"/> (Phase 6 simple version).
    /// Reset mỗi lần restart API process — KHÔNG persist.
    /// [Role: Admin, Manager]
    /// </summary>
    /// <response code="200">Snapshot metrics.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin hoặc Manager.</response>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(FeatureFlagMetricsDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public IActionResult GetMetrics()
    {
        var snapshot = new FeatureFlagMetricsDto
        {
            ActiveFlags = new FeatureFlagStatusDto
            {
                HostDepositDiscountEnabled = _featureFlagService.IsHostDepositDiscountEnabled(),
                MemberBvcPaymentEnabled = _featureFlagService.IsMemberBvcPaymentEnabled()
            },
            PilotCafeIds = _featureFlagService.GetPilotCafeIds(),
            Counters = _featureFlagService.GetAllCounters(),
            Gauges = _featureFlagService.GetAllGauges()
        };
        return NewResponse(200, "Snapshot metrics + active feature flags.", snapshot);
    }

    private static bool TryParseYearMonth(string input, out DateTime utcMonthStart)
    {
        utcMonthStart = default;

        // Accept "yyyy-MM" only. Avoid ToLowerInvariant on input to preserve case-sensitivity where needed.
        if (DateTime.TryParseExact(
                input,
                "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utcMonthStart = new DateTime(parsed.Year, parsed.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }

        return false;
    }
}