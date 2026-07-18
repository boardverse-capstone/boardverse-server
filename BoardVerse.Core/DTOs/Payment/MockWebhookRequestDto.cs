namespace BoardVerse.Core.DTOs.Payment;

public class MockWebhookRequestDto
{
    public required string OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public string? ReferenceCode { get; set; }
}
