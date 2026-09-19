namespace BoardVerse.Core.Settings;

/// <summary>
/// Cấu hình PaymentGateway: retry policy cho SePay + toggle feature.
/// Bind từ section "PaymentGateway" trong appsettings.json.
/// Spec: sepay-payment-flow.mdc §VII.1.
/// </summary>
public class PaymentGatewaySettings
{
    public const string SectionName = "PaymentGateway";

    /// <summary>
    /// Số lần retry tối đa khi SePay checkout thất bại transient.
    /// </summary>
    public int SePayMaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay base (ms) cho exponential backoff: baseDelayMs × 2^(attempt-1).
    /// </summary>
    public int SePayRetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Bật VietQR static fallback khi SePay fail hoàn toàn.
    /// </summary>
    public bool EnableVietQrFallback { get; set; } = true;

    /// <summary>
    /// Bật mock payment (bỏ qua webhook signature verification trong dev).
    /// Phải luôn = false trên production.
    /// </summary>
    public bool EnableMockPayments { get; set; } = false;

    /// <summary>
    /// Timeout cho mỗi request SePay checkout (ms).
    /// </summary>
    public int SePayTimeoutMs { get; set; } = 10000;
}
