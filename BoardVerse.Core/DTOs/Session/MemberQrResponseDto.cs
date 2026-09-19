using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Session;

/// <summary>
/// QR code info cho một thành viên — trả về cho player hiển thị trên mobile.
/// Dùng cho endpoint: GET /api/payments/split-bill/members/{memberId}/qr
/// </summary>
public class MemberQrResponseDto
{
    /// <summary>Mã thành viên.</summary>
    public Guid MemberId { get; set; }

    /// <summary>Mã phiên chơi.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Tên hiển thị của thành viên.</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>Tổng số tiền phải thanh toán.</summary>
    public decimal AmountDue { get; set; }

    /// <summary>Trạng thái thanh toán của thành viên.</summary>
    public MemberPaymentStatus Status { get; set; }

    /// <summary>Phương thức thanh toán đã chọn (QR_CODE, CASH).</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>URL hình ảnh QR code — hiển thị trên mobile.</summary>
    public string? QrImageUrl { get; set; }

    /// <summary>URL thanh toán gateway (nếu cần redirect).</summary>
    public string? PaymentUrl { get; set; }

    /// <summary>Order ID của QR payment (format: BV-MEMBER-{memberId}).</summary>
    public string? OrderId { get; set; }

    /// <summary>Số tài khoản / nội dung chuyển khoản — hiển thị trên mobile.</summary>
    public string? TransferContent { get; set; }

    /// <summary>Hướng dẫn thanh toán cho player.</summary>
    public string? PaymentInstructions { get; set; }
}
