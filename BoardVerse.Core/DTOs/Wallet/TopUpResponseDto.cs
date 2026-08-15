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

    /// <summary>
    /// Ảnh QR dạng PNG đã encode Base64 (QR do backend proxy từ vietqr.app, server-to-server
    /// — bypass CORS cho Flutter Web). Mobile có thể dùng trực tiếp <c>Image.memory(bytes)</c>;
    /// null nếu upstream vietqr.app không fetch được (client vẫn có <c>QrUrl</c> để fallback).
    /// </summary>
    public string? QrImageBase64 { get; set; }

    /// <summary>Mã order gửi sang gateway (cho tra cứu).</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Số BVC khách sẽ nhận sau khi webhook success.</summary>
    public long ExpectedBvc { get; set; }

    /// <summary>Thời điểm hết hạn đơn top-up (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Idempotency key echo.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
