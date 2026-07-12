using BoardVerse.Core.DTOs.Payment;

namespace BoardVerse.Services.IServices;

public interface IManualPaymentService
{
    /// <summary>
    /// Staff xác nhận thanh toán thủ công khi QR không hoạt động.
    /// BR-18: Hoàn cọc/sự cố vận hành - xử lý tiền mặt.
    /// </summary>
    Task<ManualPaymentConfirmResponseDto> ConfirmManualPaymentAsync(
        ManualPaymentConfirmRequestDto request,
        Guid staffId,
        CancellationToken cancellationToken = default);
}
