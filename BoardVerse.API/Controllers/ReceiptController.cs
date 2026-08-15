using BoardVerse.Core.DTOs.Receipt;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    /// <summary>
    /// API for receipts and revenue reports.
    /// P-01: Receipt Generation
    /// P-02: Revenue Report
    /// </summary>
    [ApiController]
    [Route("api/v1")]
    public abstract class BaseReceiptController : BaseApiController { }

    /// <summary>
    /// Receipt generation and revenue report endpoints.
    /// P-01: Receipt Generation API — GET /api/v1/sessions/{sessionId}/receipt
    /// P-02: Revenue Report API — GET /api/v1/cafes/{cafeId}/revenue
    /// </summary>
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public class ReceiptController : BaseReceiptController
    {
        private readonly IReceiptService _receiptService;

        public ReceiptController(IReceiptService receiptService)
        {
            _receiptService = receiptService;
        }

        /// <summary>
        /// Lấy receipt cho một phiên chơi đã thanh toán. [Role: Admin, Manager, CafeStaff]
        /// P-01: Receipt Generation API
        /// </summary>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <response code="200">Receipt chi tiết.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền truy cập.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="409">Phiên chưa được thanh toán.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("sessions/{sessionId:guid}/receipt")]
        public async Task<IActionResult> GetSessionReceipt(Guid sessionId)
        {
            var receipt = await _receiptService.GenerateSessionReceiptAsync(sessionId);
            return NewResponse(200, ApiSuccessMessages.Session.ReceiptGenerated, receipt);
        }

        /// <summary>
        /// Lấy báo cáo doanh thu theo kỳ. [Role: Admin, Manager]
        /// P-02: Revenue Report API
        /// </summary>
        /// <param name="cafeId">Mã cafe.</param>
        /// <param name="startDate">Ngày bắt đầu (yyyy-MM-dd).</param>
        /// <param name="endDate">Ngày kết thúc (yyyy-MM-dd).</param>
        /// <param name="granularity">daily|weekly|monthly. Mặc định: daily.</param>
        /// <response code="200">Báo cáo doanh thu chi tiết.</response>
        /// <response code="400">Dữ liệu không hợp lệ (ngày không hợp lệ hoặc granularity không đúng).</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền truy cập.</response>
        /// <response code="404">Không tìm thấy cafe.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("cafes/{cafeId:guid}/revenue")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetRevenueReport(
            Guid cafeId,
            [FromQuery] DateOnly startDate,
            [FromQuery] DateOnly endDate,
            [FromQuery] string granularity = "daily")
        {
            if (endDate < startDate)
            {
                return NewResponse(400, ApiErrorMessages.System.DateRangeInvalid(startDate, endDate), null);
            }

            var validGranularities = new[] { "daily", "weekly", "monthly" };
            if (!validGranularities.Contains(granularity?.ToLowerInvariant()))
            {
                return NewResponse(400, ApiErrorMessages.System.InvalidGranularity(0), null);
            }

            var report = await _receiptService.GetRevenueReportAsync(cafeId, startDate, endDate, granularity);
            return NewResponse(200, ApiSuccessMessages.Cafe.RevenueReportRetrieved, report);
        }
    }
}
