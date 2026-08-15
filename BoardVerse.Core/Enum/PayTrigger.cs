namespace BoardVerse.Core.Enum;

/// <summary>
/// Nguồn trigger cho việc đóng phiên chơi (PaySession).
/// Dùng để audit log biết session được thanh toán từ đâu.
/// </summary>
public enum PayTrigger
{
    /// <summary>Staff bấm "Thanh toán" trên POS.</summary>
    Manual = 0,

    /// <summary>SePay ping webhook khi nhận tiền từ khách CK QR.</summary>
    SePayWebhook = 1,

    /// <summary>Dev/test mock webhook (chỉ chạy trong Development env).</summary>
    MockWebhook = 2
}