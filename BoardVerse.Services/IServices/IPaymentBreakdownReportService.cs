using BoardVerse.Core.DTOs.Reports;

namespace BoardVerse.Services.IServices;

/// <summary>
/// M2 Phase 6 / Task C5.3 — Payment Breakdown Reporting service.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C5.
/// </summary>
public interface IPaymentBreakdownReportService
{
    /// <summary>
    /// Generate payment breakdown report cho khoảng thời gian [fromMonth, toMonth] (UTC months).
    /// Query trực tiếp từ MemberPaymentAuditLogs + MemberDepositAuditLogs + MemberPayments + ActiveSessions.
    /// </summary>
    /// <param name="cafeId">Optional cafe filter. Null = tất cả cafe.</param>
    /// <param name="fromMonth">Tháng bắt đầu (yyyy-MM-01 UTC). Null = tháng hiện tại.</param>
    /// <param name="toMonth">Tháng kết thúc (yyyy-MM-01 UTC). Null = tháng hiện tại.</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>Report gồm rows per (CafeId, Month) với breakdown chi tiết.</returns>
    Task<PaymentBreakdownReportDto> GenerateReportAsync(
        Guid? cafeId,
        DateTime? fromMonth,
        DateTime? toMonth,
        CancellationToken cancellationToken = default);
}
