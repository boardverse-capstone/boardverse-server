using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

public interface IPaymentService
{
    Task<CreatePaymentResponseDto> CreateDepositPaymentAsync(CreatePaymentRequestDto request, Guid userId);
    Task<RegenerateQrResponseDto> RegenerateDepositQrAsync(Guid depositId, Guid userId);
    Task<CreateSessionPaymentResponseDto> CreateSessionPaymentAsync(CreateSessionPaymentRequestDto request);
    Task<CreateSessionPaymentResponseDto> RegenerateSessionQrAsync(Guid sessionId);
    Task HandleSePayWebhookAsync(SePayWebhookDto webhook);
    Task<BookingDeposit> RefundDepositAsync(Guid depositId, string reason);
    Task ProcessExpiredDepositsAsync();
}
