using BoardVerse.Core.DTOs.Payment;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface IManualPaymentService
{
    /// <summary>
    /// Staff xác nhận thanh toán thủ công khi QR không hoạt động.
    /// BR-18: Hoàn cọc/sự cố vận hành - xử lý tiền mặt.
    /// </summary>
    /// <param name="actorRole">Role của staff (Admin / Manager / CafeStaff). Admin bypass ownership check.</param>
    Task<ManualPaymentConfirmResponseDto> ConfirmManualPaymentAsync(
        ManualPaymentConfirmRequestDto request,
        Guid staffId,
        string actorRole,
        CancellationToken cancellationToken = default);
}