namespace BoardVerse.Core.DTOs.Payment;

public class MockWebhookRequestDto
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string Status { get; set; } = "success";
    public string? ReferenceCode { get; set; }
}
