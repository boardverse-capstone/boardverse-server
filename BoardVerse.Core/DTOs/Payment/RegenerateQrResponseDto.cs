namespace BoardVerse.Core.DTOs.Payment;

public class RegenerateQrResponseDto
{
    public Guid DepositId { get; set; }
    public string PaymentUrl { get; set; } = string.Empty;
    public string QrUrl { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    /// <summary>Nội dung chuyển khoản ngẫu nhiên, dùng làm nội dung CK trên QR để webhook match đúng đơn.</summary>
    public string TransferContent { get; set; } = string.Empty;
    public DateTime? QrExpiresAt { get; set; }
    public decimal Amount { get; set; }
    public string? Gateway { get; set; }
    public bool RequiresManualConfirmation { get; set; }
}
