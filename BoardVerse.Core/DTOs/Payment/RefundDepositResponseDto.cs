namespace BoardVerse.Core.DTOs.Payment;

public class RefundDepositResponseDto
{
    public Guid DepositId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? RefundedAmount { get; set; }
    public DateTime ProcessedAt { get; set; }
}
