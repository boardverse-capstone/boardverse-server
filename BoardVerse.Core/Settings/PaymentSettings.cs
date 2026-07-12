namespace BoardVerse.Core.Settings;

public class PaymentSettings
{
    public const string SectionName = "Payment";

    public bool AllowWebhookWithoutToken { get; set; } = false;
    public bool AutoReleaseSettlementOnDepositWebhook { get; set; } = true;
}
