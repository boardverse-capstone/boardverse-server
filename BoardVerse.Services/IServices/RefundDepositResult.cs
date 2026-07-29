using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Kết quả trả về từ IPaymentService.RefundDepositAsync.
/// Bao gồm BookingDeposit sau khi update và số tiền thực tế hoàn cho khách (tính theo DepositRefundPolicy).
/// </summary>
public class RefundDepositResult
{
    public BookingDeposit Deposit { get; set; } = null!;
    public decimal RefundedAmount { get; set; }
}
