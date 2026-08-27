using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Services.Services.Payments;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface IPaymentService
{
    Task<CreatePaymentResponseDto> CreateDepositPaymentAsync(CreatePaymentRequestDto request, Guid userId, CancellationToken cancellationToken = default);
    Task<RegenerateQrResponseDto> RegenerateDepositQrAsync(Guid depositId, Guid userId, CancellationToken cancellationToken = default);
    Task<CreateSessionPaymentResponseDto> CreateSessionPaymentAsync(CreateSessionPaymentRequestDto request, Guid actorUserId, string actorRole, CancellationToken cancellationToken = default);
    Task<CreateSessionPaymentResponseDto> RegenerateSessionQrAsync(Guid sessionId, Guid actorUserId, string actorRole, CancellationToken cancellationToken = default);
    Task<(bool IsValid, string? ErrorMessage)> VerifyWebhookRequestAsync(SePayWebhookVerificationRequest request, CancellationToken cancellationToken = default);
    Task HandleSePayWebhookAsync(SePayWebhookDto webhook, CancellationToken cancellationToken = default);
    Task<RefundDepositResult> RefundDepositAsync(Guid depositId, string reason, Guid actorUserId, string actorRole, CancellationToken cancellationToken = default);
    Task ProcessExpiredDepositsAsync(CancellationToken cancellationToken = default);
}
