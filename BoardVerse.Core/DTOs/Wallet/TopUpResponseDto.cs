namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Response sau khi tạo đơn top-up thành công — trả URL payment gateway + số BVC dự kiến.
/// </summary>
public class TopUpResponseDto
{
    /// <summary>URL thanh toán SePay/VietQR.</summary>
    public string PaymentUrl { get; set; } = string.Empty;

    /// <summary>URL QR code (deposit qua SePay/VietQR).</summary>
    public string? QrUrl { get; set; }

    /// <summary>Mã order gửi sang gateway (cho tra cứu).</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Số BVC khách sẽ nhận sau khi webhook success.</summary>
    public long ExpectedBvc { get; set; }

    /// <summary>Thời điểm hết hạn đơn top-up (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Idempotency key echo.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
