namespace BoardVerse.Core.DTOs.Payment;

public class ManualPaymentConfirmRequestDto
{
    /// <summary>
    /// Loại thanh toán: Deposit hoặc Session
    /// </summary>
    public required string PaymentType { get; init; }

    /// <summary>
    /// ID của đơn cọc hoặc phiên chơi
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Số tiền thanh toán (để đối chiếu)
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Phương thức thanh toán thực tế
    /// </summary>
    public required string PaymentMethod { get; init; }

    /// <summary>
    /// Ghi chú (ví dụ: lý do không dùng QR)
    /// </summary>
    public string? Notes { get; init; }
}

public class ManualPaymentConfirmResponseDto
{
    public Guid TransactionId { get; init; }
    public string PaymentType { get; init; } = string.Empty;
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime ConfirmedAt { get; init; }
    public string ConfirmedBy { get; init; } = string.Empty;
}
