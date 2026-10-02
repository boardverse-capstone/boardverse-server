namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// Trạng thái thanh toán của từng thành viên trong group session.
    /// </summary>
    /// <remarks>
    /// M2 extension (2026-10-01): thêm <c>PaidBvc</c>, <c>PartialBvc</c>, <c>RefundedBvc</c>
    /// cho luồng Case 2 (Member BVC payment) — docs/design/host-deposit-discount-and-bvc-payment-design.md §C1.1.
    /// C2.16 (force-close) cũng sẽ thêm <c>PaidByHost</c> trong batch M2-5.
    /// Số enum KHÔNG ĐƯỢC thay đổi vì đã có data trong DB (BR §XVII.1).
    /// Project dùng <c>HasConversion&lt;int&gt;()</c> — không có PostgreSQL ENUM type,
    /// nên không cần <c>ALTER TYPE</c> trên DB khi extend (xem sql/m1_host_deposit_discount_manual.sql header).
    /// </remarks>
    public enum MemberPaymentStatus
    {
        /// <summary>Chưa thanh toán.</summary>
        NotPaid = 0,

        /// <summary>Đã thanh toán qua QR (SePay/VietQR).</summary>
        PaidQr = 1,

        /// <summary>Đã thanh toán tiền mặt.</summary>
        PaidCash = 2,

        /// <summary>M2: Đã thanh toán toàn bộ bill bằng BVC.</summary>
        PaidBvc = 3,

        /// <summary>
        /// M2: Member trả 1 phần BVC + phần còn lại bằng cash.
        /// Dùng cho luồng split BVC/cash (Task C2.7 + C3.5).
        /// </summary>
        PartialBvc = 4,

        /// <summary>
        /// M2: Đã refund BVC về wallet do bill sai / dispute.
        /// Set bởi WalletSessionPaymentService.RefundMemberBillAsync (Task C2.8).
        /// </summary>
        RefundedBvc = 5,

        /// <summary>
        /// M2/C2.16: Force-close host covers unpaid members.
        /// Set bởi <c>ForceCloseService.ForceCloseWithUnpaidAsync</c> khi Manager chọn
        /// UnpaidMemberHandling = "CompensationByHost". Host đã trả 100% bill nhóm;
        /// unpaid member còn lại được mark paid-by-host, không tính tiền sau này.
        /// (2026-10-01)
        /// </summary>
        PaidByHost = 6
    }
}
