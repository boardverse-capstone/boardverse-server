namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// Trạng thái thanh toán của từng thành viên trong group session.
    /// </summary>
    public enum MemberPaymentStatus
    {
        /// <summary>Chưa thanh toán.</summary>
        NotPaid = 0,

        /// <summary>Đã thanh toán qua QR (SePay/VietQR).</summary>
        PaidQr = 1,

        /// <summary>Đã thanh toán tiền mặt.</summary>
        PaidCash = 2
    }
}
