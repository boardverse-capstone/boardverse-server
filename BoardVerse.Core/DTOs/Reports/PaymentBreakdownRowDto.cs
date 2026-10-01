namespace BoardVerse.Core.DTOs.Reports
{
    /// <summary>
    /// Một row trong <see cref="PaymentBreakdownReportDto"/> — đại diện cho 1 cafe × 1 tháng.
    /// M2 Phase 6 / Task C5.3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Unit tiền tệ:</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><c>*Bvc</c> fields → số BVC (long), 1 BVC = 1.000 VND.</item>
    ///   <item><c>TotalCash</c> + <c>TotalQr</c> → số VND (decimal) — đọc từ MemberPayments table.</item>
    /// </list>
    /// </remarks>
    public class PaymentBreakdownRowDto
    {
        /// <summary>Cafe ID.</summary>
        public Guid CafeId { get; set; }

        /// <summary>Tháng (UTC, luôn day=1). Format ISO <c>yyyy-MM-01T00:00:00Z</c>.</summary>
        public DateTime Month { get; set; }

        /// <summary>
        /// Tổng deposit captured theo <c>HostDepositUsageMode.DiscountGroup</c> (BVC).
        /// Đọc từ MemberDepositAuditLog.Action LIKE 'Captured_OnGroupAPay' filter theo mode snapshot.
        /// </summary>
        public long TotalDiscountGroup { get; set; }

        /// <summary>
        /// Tổng deposit captured theo <c>HostDepositUsageMode.DiscountHostOnly</c> (BVC).
        /// Đọc từ MemberDepositAuditLog.Action LIKE 'Captured_OnHostDiscount'.
        /// </summary>
        public long TotalDiscountHostOnly { get; set; }

        /// <summary>
        /// Tổng BVC member thanh toán — bao gồm full BVC (<c>PaidBvc</c>) + phần BVC của partial (<c>PartialBvc</c>).
        /// Đọc từ <c>MemberPaymentAuditLog</c> WHERE PaymentMethod IN ('Bvc', 'BvcPartial').
        /// </summary>
        public long TotalMemberBvc { get; set; }

        /// <summary>
        /// Tổng phần BVC của partial payments (<c>PaymentMethod = 'BvcPartial'</c>).
        /// Subset của <see cref="TotalMemberBvc"/>. Đọc tỳ <c>MemberPaymentAuditLog</c> WHERE PaymentMethod = 'BvcPartial'.
        /// </summary>
        public long TotalMemberBvcPartial { get; set; }

        /// <summary>Tổng VND cash thu từ MemberPayments WHERE PaymentMethod = 'CASH' (VND).</summary>
        public decimal TotalCash { get; set; }

        /// <summary>Tổng VND QR thu từ MemberPayments WHERE PaymentMethod = 'QR_CODE' (VND).</summary>
        public decimal TotalQr { get; set; }

        /// <summary>
        /// Tổng BVC refund trên merge (Exception 4). Đọc từ MemberDepositAuditLog WHERE Action = 'Refunded_OnMerge'.
        /// </summary>
        public long TotalRefundMerge { get; set; }

        /// <summary>
        /// Tổng BVC refund trên bill sai / dispute. Đọc từ MemberPaymentAuditLog WHERE RefundedAt IS NOT NULL.
        /// </summary>
        public long TotalRefundBill { get; set; }

        /// <summary>Số ActiveSession PAID trong tháng (group by CreatedAt month).</summary>
        public int SessionCount { get; set; }
    }
}