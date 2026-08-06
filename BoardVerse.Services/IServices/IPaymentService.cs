using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

public interface IPaymentService
{
    Task<CreatePaymentResponseDto> CreateDepositPaymentAsync(CreatePaymentRequestDto request, Guid userId);
    Task<RegenerateQrResponseDto> RegenerateDepositQrAsync(Guid depositId, Guid userId);
    Task<CreateSessionPaymentResponseDto> CreateSessionPaymentAsync(CreateSessionPaymentRequestDto request, Guid actorUserId, string actorRole);
    Task<CreateSessionPaymentResponseDto> RegenerateSessionQrAsync(Guid sessionId, Guid actorUserId, string actorRole);
    Task HandleSePayWebhookAsync(SePayWebhookDto webhook);
    Task<RefundDepositResult> RefundDepositAsync(Guid depositId, string reason, Guid actorUserId, string actorRole);
    Task ProcessExpiredDepositsAsync();
}
