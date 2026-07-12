using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Payments;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class PaymentService : IPaymentService
{
    private readonly IBookingDepositService _depositService;
    private readonly ICafeRepository _cafeRepository;
    private readonly ICafeSettlementRepository _settlementRepository;
    private readonly IPaymentMasterAccountRepository _masterAccountRepository;
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly ISePayClient _sePayClient;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IBookingDepositService depositService,
        ICafeRepository cafeRepository,
        ICafeSettlementRepository settlementRepository,
        IPaymentMasterAccountRepository masterAccountRepository,
        IActiveSessionRepository activeSessionRepository,
        ISePayClient sePayClient,
        ILogger<PaymentService> logger)
    {
        _depositService = depositService;
        _cafeRepository = cafeRepository;
        _settlementRepository = settlementRepository;
        _masterAccountRepository = masterAccountRepository;
        _activeSessionRepository = activeSessionRepository;
        _sePayClient = sePayClient;
        _logger = logger;
    }

    public async Task<CreatePaymentResponseDto> CreateDepositPaymentAsync(CreatePaymentRequestDto request, Guid userId)
    {
        var deposit = await _depositService.GetByIdAsync(request.DepositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        if (deposit.Status != BookingDepositStatus.Pending)
        {
            throw new ConflictException("Đơn cọc đã được xử lý thanh toán trước đó.");
        }

        if (string.IsNullOrWhiteSpace(deposit.OrderId))
        {
            deposit.OrderId = GenerateOrderId(deposit.Id);
        }

        var paymentRequest = new CreatePaymentRequest(
            OrderId: deposit.OrderId,
            Amount: request.Amount,
            CustomerEmail: request.CustomerEmail,
            Description: $"BoardVerse session deposit - {deposit.OrderId}",
            Metadata: new Dictionary<string, string?>
            {
                ["depositId"] = deposit.Id.ToString(),
                ["activeSessionId"] = deposit.ActiveSessionId.ToString(),
                ["userId"] = userId.ToString()
            });

        var paymentUrl = await _sePayClient.CreatePaymentAsync(paymentRequest);

        _logger.LogInformation(
            "Payment link created for deposit. DepositId={DepositId}, PaymentUrl={PaymentUrl}",
            deposit.Id, paymentUrl);

        return new CreatePaymentResponseDto
        {
            PaymentUrl = paymentUrl,
            OrderId = deposit.OrderId
        };
    }

    /// <summary>
    /// Tạo thanh toán cho hóa đơn phiên chơi qua SePay của cafe.
    /// BR-15: TotalAmount = Subtotal + Penalty - DepositAppliedAmount
    /// Session payment dùng SePay của từng cafe (không dùng central account).
    /// </summary>
    public async Task<CreateSessionPaymentResponseDto> CreateSessionPaymentAsync(CreateSessionPaymentRequestDto request)
    {
        var session = await _activeSessionRepository.GetByIdAsync(request.SessionId)
            ?? throw new NotFoundException($"Không tìm thấy phiên chơi với ID: {request.SessionId}");

        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException("Phiên chơi phải ở trạng thái UNPAID để tạo thanh toán.");
        }

        var totalAmount = session.TotalAmount;
        if (totalAmount <= 0)
        {
            throw new ConflictException("Số tiền thanh toán phải lớn hơn 0.");
        }

        if (string.IsNullOrWhiteSpace(session.OrderId))
        {
            session.OrderId = GenerateOrderId(session.Id);
        }

        // Lấy SePay config của cafe
        var cafe = await _cafeRepository.GetByIdAsync(session.CafeId)
            ?? throw new NotFoundException($"Không tìm thấy cafe với ID: {session.CafeId}");

        if (string.IsNullOrWhiteSpace(cafe.SePayMerchantId) ||
            string.IsNullOrWhiteSpace(cafe.SePayApiKey) ||
            string.IsNullOrWhiteSpace(cafe.SePaySecretKey))
        {
            throw new PaymentException($"Cafe '{cafe.Name}' chưa được cấu hình SePay. Vui lòng liên hệ quản lý cafe.");
        }

        var cafeSePayConfig = new CafeSePayConfig(
            MerchantId: cafe.SePayMerchantId,
            ApiKey: cafe.SePayApiKey,
            SecretKey: cafe.SePaySecretKey,
            ReturnUrl: cafe.SePayReturnUrl ?? "https://rigid-boil-suffix.ngrok-free.dev/api/payments/sepay/return"
        );

        var paymentRequest = new CreatePaymentRequest(
            OrderId: session.OrderId,
            Amount: totalAmount,
            CustomerEmail: request.CustomerEmail,
            Description: $"BoardVerse session payment - {session.OrderId}",
            Metadata: new Dictionary<string, string?>
            {
                ["sessionId"] = session.Id.ToString(),
                ["cafeId"] = session.CafeId.ToString(),
                ["notes"] = request.Notes ?? string.Empty
            });

        var paymentUrl = await _sePayClient.CreatePaymentAsync(paymentRequest, cafeSePayConfig);

        _logger.LogInformation(
            "Session payment created via Cafe SePay. SessionId={SessionId}, OrderId={OrderId}, Amount={Amount}, PaymentUrl={PaymentUrl}, CafeId={CafeId}",
            session.Id, session.OrderId, totalAmount, paymentUrl, session.CafeId);

        return new CreateSessionPaymentResponseDto
        {
            SessionId = session.Id,
            PaymentUrl = paymentUrl,
            OrderId = session.OrderId,
            Amount = totalAmount,
            Status = "Pending"
        };
    }

    public async Task HandleSePayWebhookAsync(SePayWebhookDto webhook)
    {
        if (string.IsNullOrWhiteSpace(webhook.OrderId) && string.IsNullOrWhiteSpace(webhook.GatewayTransactionId))
        {
            _logger.LogWarning("SePay webhook missing order_id and gateway_transaction_id.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(webhook.Signature))
        {
            var rawBody = $"{webhook.OrderId}|{webhook.GatewayTransactionId}|{webhook.Amount}|{webhook.Status}";
            var isValid = await _sePayClient.VerifyWebhookAsync(webhook.Signature, rawBody);
            if (!isValid)
            {
                _logger.LogWarning("SePay webhook signature invalid. OrderId={OrderId}", webhook.OrderId);
                return;
            }
        }

        BookingDeposit? deposit = null;

        if (!string.IsNullOrWhiteSpace(webhook.GatewayTransactionId))
        {
            deposit = await _depositService.GetBySePayTransactionIdAsync(webhook.GatewayTransactionId.Trim());
        }

        if (deposit == null && !string.IsNullOrWhiteSpace(webhook.OrderId))
        {
            deposit = await _depositService.GetByOrderIdAsync(webhook.OrderId.Trim());
        }

        // If deposit found, process deposit webhook
        if (deposit != null)
        {
            await ProcessDepositWebhookAsync(webhook, deposit);
            return;
        }

        // Otherwise, try to process as session payment
        await ProcessSessionPaymentWebhookAsync(webhook);
    }

    private async Task ProcessDepositWebhookAsync(SePayWebhookDto webhook, BookingDeposit deposit)
    {
        var normalizedStatus = webhook.Status?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalizedStatus is "success" or "paid")
        {
            if (deposit.Status == BookingDepositStatus.Paid)
            {
                _logger.LogInformation("SePay webhook duplicate for already-paid deposit. DepositId={DepositId}", deposit.Id);
                return;
            }

            if (webhook.Amount != deposit.Amount)
            {
                _logger.LogWarning(
                    "SePay webhook amount mismatch. Expected={Expected}, Received={Received}, DepositId={DepositId}",
                    deposit.Amount, webhook.Amount, deposit.Id);
                return;
            }

            await _depositService.MarkAsPaidAsync(deposit.Id, webhook.GatewayTransactionId);
            _logger.LogInformation("Booking deposit paid. DepositId={DepositId}, Amount={Amount}", deposit.Id, deposit.Amount);
        }
        else if (normalizedStatus is "failed" or "canceled" or "cancelled")
        {
            if (deposit.Status != BookingDepositStatus.Pending)
            {
                _logger.LogInformation("SePay webhook duplicate for non-pending deposit. DepositId={DepositId}, Status={Status}",
                    deposit.Id, deposit.Status);
                return;
            }

            await _depositService.MarkAsRefundedAsync(deposit.Id);
            _logger.LogInformation("Booking deposit refunded (payment failed/cancelled). DepositId={DepositId}", deposit.Id);
        }
    }

    private async Task ProcessSessionPaymentWebhookAsync(SePayWebhookDto webhook)
    {
        if (string.IsNullOrWhiteSpace(webhook.OrderId))
        {
            _logger.LogWarning("SePay webhook for session payment missing OrderId.");
            return;
        }

        var session = await _activeSessionRepository.GetByIdAsync(webhook.SessionId ?? Guid.Empty);
        if (session == null)
        {
            // Try to find session by OrderId prefix
            var sessions = await _activeSessionRepository.GetAllUnpaidAsync();
            session = sessions.FirstOrDefault(s => s.OrderId == webhook.OrderId);
        }

        if (session == null)
        {
            _logger.LogWarning("SePay webhook session payment not matched. OrderId={OrderId}", webhook.OrderId);
            return;
        }

        var normalizedStatus = webhook.Status?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalizedStatus is "success" or "paid")
        {
            if (session.Status == GroupSessionStatus.Paid)
            {
                _logger.LogInformation("SePay webhook duplicate for already-paid session. SessionId={SessionId}", session.Id);
                return;
            }

            if (webhook.Amount != session.TotalAmount)
            {
                _logger.LogWarning(
                    "SePay webhook amount mismatch for session. Expected={Expected}, Received={Received}, SessionId={SessionId}",
                    session.TotalAmount, webhook.Amount, session.Id);
                return;
            }

            session.Status = GroupSessionStatus.Paid;
            session.PaidAt = DateTime.UtcNow;
            await _activeSessionRepository.SaveChangesAsync();

            _logger.LogInformation("Session payment completed via SePay. SessionId={SessionId}, Amount={Amount}", session.Id, session.TotalAmount);
        }
        else if (normalizedStatus is "failed" or "canceled" or "cancelled")
        {
            _logger.LogInformation("Session payment failed/cancelled. SessionId={SessionId}, Status={Status}", session.Id, normalizedStatus);
        }
    }

    /// <summary>
    /// Hoàn cọc dựa trên chính sách của quán.
    /// BR-18: Hoàn 100% khi hủy do bất khả kháng từ phía quán.
    /// BR-18: Hoàn/phạt theo RefundPolicy khi hủy từ phía khách.
    /// </summary>
    public async Task<BookingDeposit> RefundDepositAsync(Guid depositId, string reason)
    {
        var deposit = await _depositService.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        if (deposit.Status != BookingDepositStatus.Paid)
        {
            throw new ConflictException($"Không thể hoàn cọc: trạng thái hiện tại là '{deposit.Status}', cần 'Paid'.");
        }

        if (deposit.RefundPolicy == DepositRefundPolicy.None)
        {
            return await _depositService.ForfeitAsync(depositId);
        }

        return await _depositService.MarkAsRefundedAsync(depositId);
    }

    /// <summary>
    /// Xử lý đơn cọc PENDING quá hạn thanh toán (5 phút).
    /// Được gọi bởi BookingDepositExpiryJob.
    /// </summary>
    public async Task ProcessExpiredDepositsAsync()
    {
        await _depositService.ProcessExpiredDepositsAsync();
    }

    private static string GenerateOrderId(Guid depositId)
    {
        var bytes = depositId.ToByteArray();
        var hash = BitConverter.ToUInt32(bytes, 0) % 100_000_000;
        return $"BV{hash:D8}";
    }
}
