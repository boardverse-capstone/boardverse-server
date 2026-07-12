using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

public interface IPaymentService
{
    Task<CreatePaymentResponseDto> CreateDepositPaymentAsync(CreatePaymentRequestDto request, Guid userId);
    Task<CreateSessionPaymentResponseDto> CreateSessionPaymentAsync(CreateSessionPaymentRequestDto request);
    Task HandleSePayWebhookAsync(SePayWebhookDto webhook);
    Task<BookingDeposit> RefundDepositAsync(Guid depositId, string reason);
    Task ProcessExpiredDepositsAsync();
}
