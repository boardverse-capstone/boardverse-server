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
    private readonly IPaymentGatewayService _paymentGateway;
    private readonly ISePayClient _sePayClient;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IBookingDepositService depositService,
        ICafeRepository cafeRepository,
        ICafeSettlementRepository settlementRepository,
        IPaymentMasterAccountRepository masterAccountRepository,
        IActiveSessionRepository activeSessionRepository,
        IPaymentGatewayService paymentGateway,
        ISePayClient sePayClient,
        ILogger<PaymentService> logger)
    {
        _depositService = depositService;
        _cafeRepository = cafeRepository;
        _settlementRepository = settlementRepository;
        _masterAccountRepository = masterAccountRepository;
        _activeSessionRepository = activeSessionRepository;
        _paymentGateway = paymentGateway;
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

        // Get cafe config for VietQR fallback
        var cafe = await _cafeRepository.GetByIdAsync(deposit.CafeId);
        var bankCode = cafe?.SePayBankCode ?? "MBBank";
        var accountNumber = cafe?.SePayAccountNumber ?? "0000000001";

        var paymentRequest = new PaymentGatewayRequest
        {
            OrderId = deposit.OrderId,
            Amount = request.Amount,
            CustomerEmail = request.CustomerEmail,
            Description = $"BoardVerse session deposit - {deposit.OrderId}",
            Metadata = new Dictionary<string, string?>
            {
                ["depositId"] = deposit.Id.ToString(),
                ["activeSessionId"] = deposit.ActiveSessionId.ToString(),
                ["userId"] = userId.ToString()
            },
            BankCode = bankCode,
            AccountNumber = accountNumber
        };

        var result = await _paymentGateway.CreatePaymentAsync(paymentRequest);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Payment gateway failed completely. OrderId={OrderId}, Error={Error}",
                deposit.OrderId, result.ErrorMessage);
            throw new PaymentException($"Không thể tạo thanh toán: {result.ErrorMessage}");
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl;
        var qrExpiresAt = result.Gateway == PaymentGateway.SePay
            ? DateTime.UtcNow.AddMinutes(5)
            : (DateTime?)null;

        // Update deposit with new QR info
        if (qrExpiresAt.HasValue)
        {
            await _depositService.UpdateQrInfoAsync(deposit.Id, paymentUrl, qrExpiresAt.Value);
        }

        _logger.LogInformation(
            "Payment created via gateway. Gateway={Gateway}, DepositId={DepositId}, PaymentUrl={PaymentUrl}, RequiresManual={RequiresManual}",
            result.Gateway, deposit.Id, paymentUrl, result.RequiresManualConfirmation);

        return new CreatePaymentResponseDto
        {
            PaymentUrl = paymentUrl,
            OrderId = deposit.OrderId,
            QrImageUrl = result.QrImageUrl,
            Gateway = result.Gateway.ToString(),
            RequiresManualConfirmation = result.RequiresManualConfirmation,
            Message = result.Message
        };
    }

    /// <summary>
    /// Tạo lại QR thanh toán cho đơn cọc PENDING.
    /// QR cũ sẽ bị đánh dấu expired (QR URL vẫn lưu để reference).
    /// Không giới hạn số lần regenerate.
    /// Sử dụng fallback chain: SePay -> VietQR
    /// </summary>
    public async Task<RegenerateQrResponseDto> RegenerateDepositQrAsync(Guid depositId, Guid userId)
    {
        var deposit = await _depositService.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        if (deposit.Status != BookingDepositStatus.Pending)
        {
            throw new ConflictException($"Chỉ có thể tạo lại QR cho đơn cọc đang PENDING. Trạng thái hiện tại: '{deposit.Status}'.");
        }

        // Get cafe config
        var cafe = await _cafeRepository.GetByIdAsync(deposit.CafeId)
            ?? throw new NotFoundException($"Không tìm thấy cafe với ID: {deposit.CafeId}");

        var bankCode = cafe.SePayBankCode ?? "MBBank";
        var accountNumber = cafe.SePayAccountNumber ?? "0000000001";

        var paymentRequest = new PaymentGatewayRequest
        {
            OrderId = deposit.OrderId,
            Amount = deposit.Amount,
            CustomerEmail = null,
            Description = $"BoardVerse session deposit - {deposit.OrderId} (Regenerated)",
            Metadata = new Dictionary<string, string?>
            {
                ["depositId"] = deposit.Id.ToString(),
                ["activeSessionId"] = deposit.ActiveSessionId.ToString(),
                ["userId"] = userId.ToString(),
                ["regenerated"] = "true"
            },
            BankCode = bankCode,
            AccountNumber = accountNumber
        };

        var result = await _paymentGateway.CreatePaymentAsync(paymentRequest);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Payment gateway failed completely on regenerate. OrderId={OrderId}, Error={Error}",
                deposit.OrderId, result.ErrorMessage);
            throw new PaymentException($"Không thể tạo thanh toán: {result.ErrorMessage}");
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl;
        var qrExpiresAt = result.Gateway == PaymentGateway.SePay
            ? DateTime.UtcNow.AddMinutes(5)
            : (DateTime?)null;

        // Update deposit with new QR info
        if (qrExpiresAt.HasValue)
        {
            await _depositService.UpdateQrInfoAsync(depositId, paymentUrl, qrExpiresAt.Value);
        }

        _logger.LogInformation(
            "QR regenerated via gateway. Gateway={Gateway}, DepositId={DepositId}, OldQr={OldQr}, NewQrExpiresAt={QrExpiresAt}",
            result.Gateway, depositId, deposit.QrUrl, qrExpiresAt);

        return new RegenerateQrResponseDto
        {
            DepositId = deposit.Id,
            PaymentUrl = paymentUrl,
            QrUrl = result.QrImageUrl,
            OrderId = deposit.OrderId,
            QrExpiresAt = qrExpiresAt ?? DateTime.UtcNow.AddMinutes(5),
            Amount = deposit.Amount,
            Gateway = result.Gateway.ToString(),
            RequiresManualConfirmation = result.RequiresManualConfirmation
        };
    }

    /// <summary>
    /// Tạo thanh toán cho hóa đơn phiên chơi qua SePay của cafe.
    /// BR-15: TotalAmount = Subtotal + Penalty - DepositAppliedAmount
    /// Session payment dùng SePay của từng cafe (không dùng central account).
    /// Sử dụng fallback chain: SePay -> VietQR
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

        // Lấy cafe config
        var cafe = await _cafeRepository.GetByIdAsync(session.CafeId)
            ?? throw new NotFoundException($"Không tìm thấy cafe với ID: {session.CafeId}");

        if (string.IsNullOrWhiteSpace(cafe.SePayMerchantId) ||
            string.IsNullOrWhiteSpace(cafe.SePayApiKey) ||
            string.IsNullOrWhiteSpace(cafe.SePaySecretKey))
        {
            throw new PaymentException($"Cafe '{cafe.Name}' chưa được cấu hình SePay. Vui lòng liên hệ quản lý cafe.");
        }

        var paymentRequest = new PaymentGatewayRequest
        {
            OrderId = session.OrderId,
            Amount = totalAmount,
            CustomerEmail = request.CustomerEmail,
            Description = $"BoardVerse session payment - {session.OrderId}",
            Metadata = new Dictionary<string, string?>
            {
                ["sessionId"] = session.Id.ToString(),
                ["cafeId"] = session.CafeId.ToString(),
                ["notes"] = request.Notes ?? string.Empty
            },
            BankCode = cafe.SePayBankCode ?? "MBBank",
            AccountNumber = cafe.SePayAccountNumber ?? "0000000001"
        };

        var result = await _paymentGateway.CreatePaymentAsync(paymentRequest);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Payment gateway failed completely for session. SessionId={SessionId}, Error={Error}",
                session.Id, result.ErrorMessage);
            throw new PaymentException($"Không thể tạo thanh toán: {result.ErrorMessage}");
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl;

        _logger.LogInformation(
            "Session payment created via gateway. Gateway={Gateway}, SessionId={SessionId}, Amount={Amount}, RequiresManual={RequiresManual}",
            result.Gateway, session.Id, totalAmount, result.RequiresManualConfirmation);

        return new CreateSessionPaymentResponseDto
        {
            SessionId = session.Id,
            PaymentUrl = paymentUrl,
            QrImageUrl = result.QrImageUrl,
            OrderId = session.OrderId,
            Amount = totalAmount,
            Status = "Pending",
            Gateway = result.Gateway.ToString(),
            RequiresManualConfirmation = result.RequiresManualConfirmation
        };
    }

    /// <summary>
    /// Tạo lại QR thanh toán cho phiên chơi đang UNPAID.
    /// QR cũ sẽ bị đánh dấu là EXPIRED.
    /// Sử dụng fallback chain: SePay -> VietQR
    /// </summary>
    public async Task<CreateSessionPaymentResponseDto> RegenerateSessionQrAsync(Guid sessionId)
    {
        var session = await _activeSessionRepository.GetByIdAsync(sessionId)
            ?? throw new NotFoundException($"Không tìm thấy phiên chơi với ID: {sessionId}");

        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException("Phiên chơi phải ở trạng thái UNPAID để tạo lại QR.");
        }

        // Lấy cafe config
        var cafe = await _cafeRepository.GetByIdAsync(session.CafeId)
            ?? throw new NotFoundException($"Không tìm thấy cafe với ID: {session.CafeId}");

        if (string.IsNullOrWhiteSpace(cafe.SePayMerchantId) ||
            string.IsNullOrWhiteSpace(cafe.SePayApiKey) ||
            string.IsNullOrWhiteSpace(cafe.SePaySecretKey))
        {
            throw new PaymentException($"Cafe '{cafe.Name}' chưa được cấu hình SePay.");
        }

        // Tạo order ID mới nếu chưa có
        if (string.IsNullOrWhiteSpace(session.OrderId))
        {
            session.OrderId = GenerateOrderId(session.Id);
        }

        var paymentRequest = new PaymentGatewayRequest
        {
            OrderId = session.OrderId,
            Amount = session.TotalAmount,
            CustomerEmail = null,
            Description = $"BoardVerse session payment - {session.OrderId}",
            Metadata = new Dictionary<string, string?>
            {
                ["sessionId"] = session.Id.ToString(),
                ["cafeId"] = session.CafeId.ToString(),
                ["regenerated"] = "true"
            },
            BankCode = cafe.SePayBankCode ?? "MBBank",
            AccountNumber = cafe.SePayAccountNumber ?? "0000000001"
        };

        var result = await _paymentGateway.CreatePaymentAsync(paymentRequest);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Payment gateway failed completely for session regenerate. SessionId={SessionId}, Error={Error}",
                session.Id, result.ErrorMessage);
            throw new PaymentException($"Không thể tạo thanh toán: {result.ErrorMessage}");
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl;

        _logger.LogInformation(
            "Session QR regenerated via gateway. Gateway={Gateway}, SessionId={SessionId}, Amount={Amount}",
            result.Gateway, session.Id, session.TotalAmount);

        return new CreateSessionPaymentResponseDto
        {
            SessionId = session.Id,
            PaymentUrl = paymentUrl,
            QrImageUrl = result.QrImageUrl,
            OrderId = session.OrderId,
            Amount = session.TotalAmount,
            Status = "Pending",
            Gateway = result.Gateway.ToString(),
            RequiresManualConfirmation = result.RequiresManualConfirmation
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
