using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Services.IServices;

public interface IBookingDepositService
{
    /// <summary>
    /// Tạo đơn cọc mới với validation BR-02, BR-03.
    /// BR-05: bookingId để liên kết deposit với booking (chỉ Host mới đặt cọc).
    /// </summary>
    Task<BookingDeposit> CreateAsync(
        Guid userId,
        Guid cafeId,
        Guid cafeManagerId,
        decimal amount,
        DepositRefundPolicy refundPolicy,
        DateTime? scheduledAt = null,
        Guid? bookingId = null);

    /// <summary>
    /// Đánh dấu đơn cọc là đã thanh toán thành công.
    /// BR-05: Kích hoạt khi nhận webhook success từ SePay.
    /// </summary>
    Task<BookingDeposit> MarkAsPaidAsync(Guid depositId, string? sePayTransactionId = null);

    /// <summary>
    /// Đánh dấu đơn cọc là đã hoàn tiền (full hoặc partial).
    /// BR-18: Hoàn theo chính sách của quán.
    /// </summary>
    Task<BookingDeposit> MarkAsRefundedAsync(Guid depositId);

    /// <summary>
    /// Đánh dấu đơn cọc bị tịch thu (no-refund policy).
    /// BR-18: Khi khách hủy với chính sách None.
    /// </summary>
    Task<BookingDeposit> ForfeitAsync(Guid depositId);

    /// <summary>
    /// Đánh dấu đơn cọc đã hết hạn (quá 5 phút không thanh toán).
    /// </summary>
    Task ExpireAsync(Guid depositId);

    /// <summary>
    /// Xử lý hàng loạt các đơn cọc PENDING quá hạn.
    /// Được gọi bởi BookingDepositExpiryJob.
    /// </summary>
    Task ProcessExpiredDepositsAsync();

    /// <summary>
    /// Lấy đơn cọc theo ID.
    /// </summary>
    Task<BookingDeposit?> GetByIdAsync(Guid depositId);

    /// <summary>
    /// Lấy đơn cọc theo OrderId.
    /// </summary>
    Task<BookingDeposit?> GetByOrderIdAsync(string orderId);

    /// <summary>
    /// Lấy đơn cọc theo SePay Transaction ID.
    /// </summary>
    Task<BookingDeposit?> GetBySePayTransactionIdAsync(string sePayTransactionId);

    /// <summary>BR-05: Lấy đơn cọc theo BookingId.</summary>
    Task<BookingDeposit?> GetByBookingIdAsync(Guid bookingId);

    /// <summary>
    /// Tính số tiền hoàn cọc theo BR-18 và thời gian đã trôi qua kể từ khi tạo đơn.
    /// Áp dụng cho DepositRefundPolicy = Partial.
    /// - elapsedHours >= 24 → hoàn 50%
    /// - elapsedHours >= 12 → hoàn 25%
    /// - elapsedHours &lt; 12 → hoàn 0%
    /// Trả về 0 cho các policy khác (Full thì controller tự tính = Amount; None = 0).
    /// </summary>
    decimal CalculatePartialRefundAmount(BookingDeposit deposit);

    /// <summary>
    /// Cập nhật QR URL, thời hạn và nội dung chuyển khoản cho đơn cọc.
    /// qrExpiresAt = null khi dùng VietQR tĩnh (QR không hết hạn).
    /// transferContent = nội dung CK dùng trên QR, random unique để webhook match đúng đơn.
    /// </summary>
    Task UpdateQrInfoAsync(Guid depositId, string qrUrl, DateTime? qrExpiresAt, string? transferContent = null);
}
