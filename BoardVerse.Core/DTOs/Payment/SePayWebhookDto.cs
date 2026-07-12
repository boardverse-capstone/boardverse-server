namespace BoardVerse.Core.DTOs.Payment;

public class SePayWebhookDto
{
    public string Id { get; set; } = string.Empty;
    public string Gateway { get; set; } = "SePay";
    public string? GatewayTransactionId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public string? BankCode { get; set; }
    public string? BankAccount { get; set; }
    public string? Note { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? Signature { get; set; }
    public Guid? SessionId { get; set; }
}
