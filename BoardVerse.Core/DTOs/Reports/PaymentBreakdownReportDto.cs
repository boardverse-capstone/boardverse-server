namespace BoardVerse.Core.DTOs.Reports
{
    /// <summary>
    /// Báo cáo tổng hợp payment breakdown per cafe per month.
    /// M2 Phase 6 / Task C5.3 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C5.
    ///
    /// <para>Các cột aggregate (số tiền VND hoặc BVC, xem <see cref="PaymentBreakdownRowDto"/> để biết unit):</para>
    /// <list type="bullet">
    ///   <item><c>TotalDiscountGroup</c> — tổng deposit captured theo mode DiscountGroup.</item>
    ///   <item><c>TotalDiscountHostOnly</c> — tổng deposit captured theo mode DiscountHostOnly.</item>
    ///   <item><c>TotalMemberBvc</c> — tổng BVC member thanh toán (full BVC + partial BVC).</item>
    ///   <item><c>TotalMemberBvcPartial</c> — tổng phần BVC của partial payments (subset của TotalMemberBvc).</item>
    ///   <item><c>TotalCash</c> — tổng cash thu từ MemberPayments.</item>
    ///   <item><c>TotalQr</c> — tổng QR (SePay/VietQR) thu từ MemberPayments.</item>
    ///   <item><c>TotalRefundMerge</c> — tổng BVC refund trên merge (MemberDepositAuditLog Action = Refunded_OnMerge).</item>
    ///   <item><c>TotalRefundBill</c> — tổng BVC refund trên bill sai (MemberPaymentAuditLog RefundedAt set).</item>
    ///   <item><c>SessionCount</c> — số session PAID trong tháng (group by CreatedAt month).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// V1 limitation: KHÔNG filter theo cafe staff ownership (UserId in (...)).
    /// Tất cả admin có thể xem toàn bộ data. Khi cần per-cafe-staff, tích hợp <c>CafeStaff</c> filter mapping ở Phase sau.
    /// </para>
    /// </remarks>
    public class PaymentBreakdownReportDto
    {
        /// <summary>Cafe filter áp dụng (null = tất cả cafe). Tương ứng query param <c>cafeId</c>.</summary>
        public Guid? CafeId { get; set; }

        /// <summary>Tháng bắt đầu (yyyy-MM-01 inclusive). Tương ứng query param <c>fromMonth</c>.</summary>
        public DateTime FromMonth { get; set; }

        /// <summary>Tháng kết thúc (yyyy-MM-01 inclusive). Tương ứng query param <c>toMonth</c>.</summary>
        public DateTime ToMonth { get; set; }

        /// <summary>Danh sách row breakdown theo từng cafe × từng tháng.</summary>
        public List<PaymentBreakdownRowDto> Rows { get; set; } = new();

        /// <summary>Thời điểm report được generate (UTC).</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}