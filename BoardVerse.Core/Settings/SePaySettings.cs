namespace BoardVerse.Core.Settings;

public class SePaySettings
{
    public const string SectionName = "SePay";

    public string Environment { get; set; } = "Sandbox";
    public string MerchantId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookToken { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;

    public string CheckoutBaseUrl => string.Equals(Environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
        ? "https://pay-sandbox.sepay.vn"
        : "https://pay.sepay.vn";

    public string ApiBaseUrl => string.Equals(Environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
        ? "https://pgapi-sandbox.sepay.vn"
        : "https://pgapi.sepay.vn";
}
