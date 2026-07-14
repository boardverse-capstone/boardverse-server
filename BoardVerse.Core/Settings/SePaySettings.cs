namespace BoardVerse.Core.Settings;

/// <summary>
/// Cấu hình thanh toán BoardVerse.
/// Checkout: dùng VietQR tĩnh (bank info ở đây).
/// Settlement (chuyển tiền cho cafe): dùng SePay Transfer API (MerchantId/SecretKey/WebhookToken).
/// </summary>
public class SePaySettings
{
    public const string SectionName = "SePay";

    /// <summary>Environment: Sandbox / Production</summary>
    public string Environment { get; set; } = "Sandbox";

    /// <summary>SePay Merchant ID — dùng cho Transfer API (settlement).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>SePay Secret Key — dùng cho Transfer API.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Webhook Token — dùng để xác minh webhook từ SePay/VietQR.</summary>
    public string WebhookToken { get; set; } = string.Empty;

    /// <summary>SePay API Base URL — dùng cho Transfer API.</summary>
    public string ApiBaseUrl { get; set; } = "https://pgapi.sepay.vn";

    /// <summary>Mã ngân hàng hiển thị trên VietQR (VD: MBBank).</summary>
    public string BankCode { get; set; } = "MBBank";

    /// <summary>Số tài khoản hiển thị trên VietQR.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Tên chủ tài khoản hiển thị trên VietQR.</summary>
    public string AccountHolder { get; set; } = string.Empty;
}
