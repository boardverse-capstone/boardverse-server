namespace BoardVerse.Core.DTOs.Payment;

public class ConfirmPaymentResponseDto
{
    public Guid DepositId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public string? QrImageUrl { get; set; }
    public string? Gateway { get; set; }
    public bool RequiresManualConfirmation { get; set; }
    public string? Message { get; set; }
}
