namespace BoardVerse.Core.DTOs.Payment;

public class CreatePaymentRequestDto
{
    public Guid DepositId { get; set; }
    public decimal Amount { get; set; }
    public string? CustomerEmail { get; set; }
    public string? Description { get; set; }
    public string? ReturnUrl { get; set; }
    public string? CancelUrl { get; set; }
}
