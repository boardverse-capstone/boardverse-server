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

        /// <summary>
        /// M2/C2.15 — Generate a per-member receipt for a paid session (Gap #32).
        /// Build the <see cref="MemberReceiptDto"/> cho một thành viên cụ thể,
        /// serialize k sang JSON, wrap trong <see cref="ReceiptFileDto"/> bytes.
        /// <para>
        /// Hiện tại chỉ hỗ trợ format <c>json</c>. PDF/PNG sẽ được tích hợp ở release sau
        /// khi bổ sung QuestPDF dependency — request format <c>pdf</c>/<c>png</c> sẽ trả về
        /// 400 BadRequest với message rõ ràng (xem <c>ApiErrorMessages.Receipt.UnsupportedReceiptFormat</c>).
        /// </para>
        /// </summary>
        /// <param name="sessionId">ActiveSession.Id.</param>
        /// <param name="memberId">ActiveSessionMember.Id.</param>
        /// <param name="format">Định dạng output. Hiện tại chỉ chấp nhận <c>json</c>; mặc định từ controller.</param>
        /// <param name="cancellationToken">Token hủy.</param>
        /// <returns>Receipt file (bytes + content type <c>application/json</c> + filename <c>.json</c>).</returns>
        /// <exception cref="BoardVerse.Core.Exceptions.BadRequestException">Format không hợp lệ (khác phải 'json').</exception>
        /// <exception cref="BoardVerse.Core.Exceptions.NotFoundException">Session hoặc member không tồn tại.</exception>
        /// <exception cref="BoardVerse.Core.Exceptions.ConflictException">Session chưa thanh toán (status =! Paid).</exception>
        Task<ReceiptFileDto> GenerateMemberReceiptAsync(
            Guid sessionId,
            Guid memberId,
            string format,
            CancellationToken cancellationToken = default);
    }
}
