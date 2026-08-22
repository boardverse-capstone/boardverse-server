using BoardVerse.Core.DTOs.Receipt;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    /// <summary>
    /// Service for generating receipts and revenue reports.
    /// P-01: Receipt Generation
    /// P-02: Revenue Report
    /// </summary>
    public interface IReceiptService
    {
        /// <summary>
        /// Generate a receipt for a paid session.
        /// </summary>
        /// <param name="sessionId">The session ID.</param>
        /// <returns>Session receipt with member breakdown.</returns>
        Task<SessionReceiptDto> GenerateSessionReceiptAsync(Guid sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get revenue report for a cafe within a date range.
        /// </summary>
        /// <param name="cafeId">The cafe ID.</param>
        /// <param name="startDate">Report start date.</param>
        /// <param name="endDate">Report end date.</param>
        /// <param name="granularity">daily|weekly|monthly</param>
        /// <returns>Revenue report with breakdowns.</returns>
        Task<RevenueReportDto> GetRevenueReportAsync(Guid cafeId, DateOnly startDate, DateOnly endDate, string granularity, CancellationToken cancellationToken = default);
    }
}
