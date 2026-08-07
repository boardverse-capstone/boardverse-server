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
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly IPaymentGatewayService _paymentGateway;
    private readonly ISePayClient _sePayClient;
    private readonly ISePayAccountService _sePayAccountService;
    private readonly IWalletService _walletService; // BVC top-up webhook
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IBookingDepositService depositService,
        ICafeRepository cafeRepository,
        ICafeSettlementRepository settlementRepository,
        IActiveSessionRepository activeSessionRepository,
        IPaymentGatewayService paymentGateway,
        ISePayClient sePayClient,
        ISePayAccountService sePayAccountService,
        IWalletService walletService,
        ILogger<PaymentService> logger)
    {
        _depositService = depositService;
        _cafeRepository = cafeRepository;
        _settlementRepository = settlementRepository;
        _activeSessionRepository = activeSessionRepository;
        _paymentGateway = paymentGateway;
        _sePayClient = sePayClient;
        _sePayAccountService = sePayAccountService;
        _walletService = walletService;
        _logger = logger;
    }

    public async Task<CreatePaymentResponseDto> CreateDepositPaymentAsync(CreatePaymentRequestDto request, Guid userId)
    {
        var deposit = await _depositService.GetByIdAsync(request.DepositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        // C1: Verify deposit ownership - only the deposit owner can create payment for it.
        if (deposit.UserId != userId)
        {
            throw new ForbiddenException(ApiErrorMessages.Payment.NotAuthorizedToViewDeposit);
        }

        if (deposit.Status != BookingDepositStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.Pos.DepositAlreadyProcessed);
        }

        // C2: Use server-side amount from deposit, never trust client-provided amount.
        if (deposit.Amount <= 0)
        {
            throw new ConflictException(ApiErrorMessages.Pos.DepositAmountMustBePositive);
        }

        if (string.IsNullOrWhiteSpace(deposit.OrderId))
        {
            deposit.OrderId = GenerateOrderId(deposit.Id);
        }

        // Sinh TransferContent ngẫu nhiên để khách nhập khi chuyển khoản ngân hàng
        var transferContent = $"BV-{Guid.NewGuid():N}";

        // Deposit payment: Lấy bank info từ DB (Master Account)
        var bankCode = string.Empty;
        var accountNumber = string.Empty;
        var accountHolder = string.Empty;

        var masterAccount = await _sePayAccountService.GetRawMasterAccountAsync();
        if (masterAccount != null)
        {
            bankCode = masterAccount.BankCode ?? string.Empty;
            // Dùng raw AccountNumber (không mask) cho VietQR — QR phải trỏ vào STK thật
            accountNumber = masterAccount.AccountNumber ?? string.Empty;
            accountHolder = masterAccount.AccountHolder ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayMasterAccountNotFound);
        }

        var paymentRequest = new PaymentGatewayRequest
        {
            OrderId = deposit.OrderId,
            // C2: Use server-side deposit amount; never trust client-supplied amount.
            Amount = deposit.Amount,
            CustomerEmail = request.CustomerEmail,
            Description = transferContent,
            Metadata = new Dictionary<string, string?>
            {
                ["depositId"] = deposit.Id.ToString(),
                ["bookingId"] = deposit.BookingId.ToString(),
                ["activeSessionId"] = deposit.ActiveSessionId.ToString(),
                ["userId"] = userId.ToString()
            },
            BankCode = bankCode,
            AccountNumber = accountNumber,
            AccountName = accountHolder
        };

        var result = await _paymentGateway.CreatePaymentAsync(paymentRequest);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Payment gateway failed. OrderId={OrderId}, Error={Error}",
                deposit.OrderId, result.ErrorMessage);
            throw new PaymentException(ApiErrorMessages.Payment.GatewayCannotCreatePaymentWithError(result.ErrorMessage));
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl ?? throw new PaymentException(ApiErrorMessages.Payment.GatewayQrUrlMissing);
        // VietQR tĩnh không có expiry — QR luôn hợp lệ
        await _depositService.UpdateQrInfoAsync(deposit.Id, paymentUrl, null, transferContent);

        _logger.LogInformation(
            "Payment created. DepositId={DepositId}, OrderId={OrderId}, Amount={Amount}, QrUrl={QrUrl}",
            deposit.Id, deposit.OrderId, deposit.Amount, paymentUrl);

        return new CreatePaymentResponseDto
        {
            PaymentUrl = paymentUrl,
            OrderId = deposit.OrderId,
            TransferContent = transferContent,
            QrImageUrl = result.QrImageUrl,
            Gateway = result.Gateway.ToString(),
            RequiresManualConfirmation = result.RequiresManualConfirmation,
            Message = result.Message
        };
    }

    /// <summary>
    /// Tạo lại QR thanh toán cho đơn cọc PENDING.
    /// QR cũ sẽ bị đánh dấu expired (QR URL vẫn lưu để reference).
    /// P2 Fix #11: Thêm rate limiting - không cho phép regenerate quá 1 lần trong 60 giây.
    /// Sử dụng fallback chain: SePay -> VietQR
    /// </summary>
    public async Task<RegenerateQrResponseDto> RegenerateDepositQrAsync(Guid depositId, Guid userId)
    {
        var deposit = await _depositService.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        // C1: Verify deposit ownership - only the deposit owner can regenerate QR.
        if (deposit.UserId != userId)
        {
            throw new ForbiddenException(ApiErrorMessages.Payment.NotAuthorizedToViewDeposit);
        }

        if (deposit.Status != BookingDepositStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.Payment.QrRegenerateInvalidState(deposit.Status.ToString()));
        }

        // P2 Fix #11: Rate limiting - chỉ cho phép regenerate 1 lần mỗi 60 giây
        if (deposit.LastQrRegeneratedAt.HasValue)
        {
            var elapsed = DateTime.UtcNow - deposit.LastQrRegeneratedAt.Value;
            if (elapsed.TotalSeconds < 60)
            {
                throw new ConflictException(ApiErrorMessages.Payment.QrRegenerateRateLimited(60 - (int)elapsed.TotalSeconds));
            }
        }

        // Deposit regeneration: Lấy bank info từ DB (Master Account)
        var bankCode = string.Empty;
        var accountNumber = string.Empty;
        var accountHolder = string.Empty;

        var masterAccount = await _sePayAccountService.GetRawMasterAccountAsync();
        if (masterAccount != null)
        {
            bankCode = masterAccount.BankCode ?? string.Empty;
            // Dùng raw AccountNumber (không mask) cho VietQR — QR phải trỏ vào STK thật
            accountNumber = masterAccount.AccountNumber ?? string.Empty;
            accountHolder = masterAccount.AccountHolder ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayMasterAccountNotFound);
        }

        // Sinh TransferContent ngẫu nhiên mới cho mỗi lần tạo QR
        var transferContent = $"BV-{Guid.NewGuid():N}";

        var paymentRequest = new PaymentGatewayRequest
        {
            OrderId = deposit.OrderId,
            Amount = deposit.Amount,
            CustomerEmail = null,
            Description = transferContent,
            Metadata = new Dictionary<string, string?>
            {
                ["depositId"] = deposit.Id.ToString(),
                ["activeSessionId"] = deposit.ActiveSessionId.ToString(),
                ["userId"] = userId.ToString(),
                ["regenerated"] = "true"
            },
            BankCode = bankCode,
            AccountNumber = accountNumber,
            AccountName = accountHolder
        };

        var result = await _paymentGateway.CreatePaymentAsync(paymentRequest);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Payment gateway failed on regenerate. OrderId={OrderId}, Error={Error}",
                deposit.OrderId, result.ErrorMessage);
            throw new PaymentException(ApiErrorMessages.Payment.GatewayCannotCreatePaymentWithError(result.ErrorMessage));
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl ?? throw new PaymentException(ApiErrorMessages.Payment.GatewayQrUrlMissing);
        // VietQR tĩnh không có expiry
        await _depositService.UpdateQrInfoAsync(depositId, paymentUrl, null, transferContent);

        _logger.LogInformation(
            "QR regenerated. DepositId={DepositId}, OldQr={OldQr}, NewQr={NewQr}",
            depositId, deposit.QrUrl, paymentUrl);

        return new RegenerateQrResponseDto
        {
            DepositId = deposit.Id,
            PaymentUrl = paymentUrl,
            QrUrl = result.QrImageUrl,
            OrderId = deposit.OrderId,
            TransferContent = transferContent,
            QrExpiresAt = null,
            Amount = deposit.Amount,
            Gateway = result.Gateway.ToString(),
            RequiresManualConfirmation = result.RequiresManualConfirmation
        };
    }

    /// <summary>
    /// Tạo thanh toán cho hóa đơn phiên chơi qua VietQR tĩnh của cafe.
    /// BR-15: TotalAmount = Subtotal + Penalty - DepositAppliedAmount
    /// Session payment dùng VietQR của từng cafe (bank info từ Cafe.SePayBankCode / SePayAccountNumber).
    /// </summary>
    public async Task<CreateSessionPaymentResponseDto> CreateSessionPaymentAsync(CreateSessionPaymentRequestDto request, Guid actorUserId, string actorRole)
    {
        var session = await _activeSessionRepository.GetByIdAsync(request.SessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.ActiveSessionNotFound(request.SessionId));

        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException(ApiErrorMessages.Pos.SessionPaymentInvalidState);
        }

        // Lấy cafe config
        var cafe = await _cafeRepository.GetByIdAsync(session.CafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.CafeRecordNotFound(session.CafeId));

        // C4: Validate cafe ownership. Manager: cafe.ManagerId == actorUserId.
        // CafeStaff: phải có cafe trong StaffMembers. Admin bypasses.
        await VerifyCafeOperatorAsync(cafe, actorUserId, actorRole);

        var totalAmount = session.TotalAmount;
        if (totalAmount <= 0)
        {
            throw new ConflictException(ApiErrorMessages.Payment.SessionPaymentAmountMustBePositive);
        }

        if (string.IsNullOrWhiteSpace(session.OrderId))
        {
            session.OrderId = GenerateOrderId(session.Id);
        }

        // Sinh TransferContent ngẫu nhiên để khách nhập khi chuyển khoản
        if (string.IsNullOrWhiteSpace(session.TransferContent))
        {
            session.TransferContent = $"BV-{Guid.NewGuid():N}";
        }

        var bankCode = string.Empty;
        var accountNumber = string.Empty;

        // Lấy từ SePayAccount nếu cafe đã được configure
        if (cafe.SePayAccountId.HasValue)
        {
            var sepayAccount = await _sePayAccountService.GetRawByCafeIdAsync(cafe.Id);
            if (sepayAccount != null)
            {
                bankCode = sepayAccount.BankCode ?? string.Empty;
                // Dùng raw AccountNumber cho VietQR
                accountNumber = sepayAccount.AccountNumber ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new PaymentException(ApiErrorMessages.Payment.PaymentCafeNotConfiguredSePay(cafe.Name));
        }

        var paymentRequest = new PaymentGatewayRequest
        {
            OrderId = session.OrderId,
            Amount = totalAmount,
            CustomerEmail = request.CustomerEmail,
            Description = session.TransferContent,
            Metadata = new Dictionary<string, string?>
            {
                ["sessionId"] = session.Id.ToString(),
                ["cafeId"] = session.CafeId.ToString(),
                ["sepayAccountId"] = cafe.SePayAccountId.ToString(),
                ["notes"] = request.Notes ?? string.Empty
            },
            BankCode = bankCode,
            AccountNumber = accountNumber,
            AccountName = cafe.Name
        };

        var result = await _paymentGateway.CreatePaymentAsync(paymentRequest);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Payment gateway failed for session. SessionId={SessionId}, Error={Error}",
                session.Id, result.ErrorMessage);
            throw new PaymentException(ApiErrorMessages.Payment.GatewayCannotCreatePaymentWithError(result.ErrorMessage));
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl ?? throw new PaymentException(ApiErrorMessages.Payment.GatewayQrUrlMissing);

        await _activeSessionRepository.UpdateAsync(session);
        await _activeSessionRepository.SaveChangesAsync();

        _logger.LogInformation(
            "Session payment created via gateway. Gateway={Gateway}, SessionId={SessionId}, Amount={Amount}, RequiresManual={RequiresManual}",
            result.Gateway, session.Id, totalAmount, result.RequiresManualConfirmation);

        return new CreateSessionPaymentResponseDto
        {
            SessionId = session.Id,
            PaymentUrl = paymentUrl,
            QrImageUrl = result.QrImageUrl,
            OrderId = session.OrderId,
            TransferContent = session.TransferContent,
            Amount = totalAmount,
            Status = "Pending",
            Gateway = result.Gateway.ToString(),
            RequiresManualConfirmation = result.RequiresManualConfirmation
        };
    }

    /// <summary>
    /// Tạo lại QR thanh toán cho phiên chơi đang UNPAID.
    /// </summary>
    public async Task<CreateSessionPaymentResponseDto> RegenerateSessionQrAsync(Guid sessionId, Guid actorUserId, string actorRole)
    {
        var session = await _activeSessionRepository.GetByIdAsync(sessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.ActiveSessionNotFound(sessionId));

        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException(ApiErrorMessages.Pos.SessionPaymentInvalidState);
        }

        // Lấy cafe config
        var cafe = await _cafeRepository.GetByIdAsync(session.CafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.CafeRecordNotFound(session.CafeId));

        // C5: Validate cafe ownership (cùng pattern với CreateSessionPaymentAsync).
        await VerifyCafeOperatorAsync(cafe, actorUserId, actorRole);

        var bankCode = string.Empty;
        var accountNumber = string.Empty;

        // Lấy từ SePayAccount nếu cafe đã được configure
        if (cafe.SePayAccountId.HasValue)
        {
            var sepayAccount = await _sePayAccountService.GetRawByCafeIdAsync(cafe.Id);
            if (sepayAccount != null)
            {
                bankCode = sepayAccount.BankCode ?? string.Empty;
                // Dùng raw AccountNumber cho VietQR
                accountNumber = sepayAccount.AccountNumber ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new PaymentException(ApiErrorMessages.Payment.PaymentCafeNotConfiguredSePay(cafe.Name));
        }

        // Tạo order ID mới nếu chưa có
        if (string.IsNullOrWhiteSpace(session.OrderId))
        {
            session.OrderId = GenerateOrderId(session.Id);
        }

        // Sinh TransferContent ngẫu nhiên mới cho mỗi lần tạo QR
        var transferContent = $"BV-{Guid.NewGuid():N}";

        var paymentRequest = new PaymentGatewayRequest
        {
            OrderId = session.OrderId,
            Amount = session.TotalAmount,
            CustomerEmail = null,
            Description = transferContent,
            Metadata = new Dictionary<string, string?>
            {
                ["sessionId"] = session.Id.ToString(),
                ["cafeId"] = session.CafeId.ToString(),
                ["sepayAccountId"] = cafe.SePayAccountId.ToString(),
                ["regenerated"] = "true"
            },
            BankCode = bankCode,
            AccountNumber = accountNumber,
            AccountName = cafe.Name
        };

        var result = await _paymentGateway.CreatePaymentAsync(paymentRequest);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Payment gateway failed completely for session regenerate. SessionId={SessionId}, Error={Error}",
                session.Id, result.ErrorMessage);
            throw new PaymentException(ApiErrorMessages.Payment.GatewayCannotCreatePaymentWithError(result.ErrorMessage));
        }

        var paymentUrl = result.PaymentUrl ?? result.QrImageUrl ?? throw new PaymentException(ApiErrorMessages.Payment.GatewayQrUrlMissing);

        // Lưu TransferContent mới vào DB
        session.TransferContent = transferContent;
        await _activeSessionRepository.UpdateAsync(session);
        await _activeSessionRepository.SaveChangesAsync();

        _logger.LogInformation(
            "Session QR regenerated via gateway. Gateway={Gateway}, SessionId={SessionId}, Amount={Amount}",
            result.Gateway, session.Id, session.TotalAmount);

        return new CreateSessionPaymentResponseDto
        {
            SessionId = session.Id,
            PaymentUrl = paymentUrl,
            QrImageUrl = result.QrImageUrl,
            OrderId = session.OrderId,
            TransferContent = transferContent,
            Amount = session.TotalAmount,
            Status = "Pending",
            Gateway = result.Gateway.ToString(),
            RequiresManualConfirmation = result.RequiresManualConfirmation
        };
    }

    public async Task HandleSePayWebhookAsync(SePayWebhookDto webhook)
    {
        // SePay BankAPINotify không gửi OrderId/Status riêng — đã được Normalize() tại
        // Controller derive từ content + transferType. Nếu vẫn rỗng (legacy mock cũ hoặc
        // payload lỗi) thì bỏ qua.
        if (string.IsNullOrWhiteSpace(webhook.OrderId) && string.IsNullOrWhiteSpace(webhook.GatewayTransactionId))
        {
            _logger.LogWarning("SePay webhook missing order_id and gateway_transaction_id. Content={Content}", webhook.Content);
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

        // Phase 2: BVC top-up — OrderId prefix "BVC-" (top-up mới derive từ content).
        // Lưu ý: SePay BankAPINotify strip dấu '-' khỏi content, nên OrderId thật
        // (dạng BVC-B796952CBCEC4E) KHÔNG còn trong content. Cần tìm userId hash từ
        // pattern "BVCTOPUP{8-hex}" trong content để match pending top-up request.
        if (!string.IsNullOrWhiteSpace(webhook.OrderId)
            && webhook.OrderId.StartsWith("BVC-", StringComparison.OrdinalIgnoreCase))
        {
            var bvcAmount = (long)(webhook.Amount / 1000m);
            await _walletService.HandleTopUpWebhookAsync(
                orderId: webhook.OrderId,
                gatewayTransactionId: webhook.GatewayTransactionId ?? string.Empty,
                amountBvc: bvcAmount,
                status: webhook.Status);
            return;
        }

        // W-07: BVC top-up qua OrderId exact match (transferContent = "BVC-{18hex}").
        // SePay strip dấu '-' → content chứa "BVC{18hex}". Tìm OrderId từ đây.
        if (!string.IsNullOrWhiteSpace(webhook.Content))
        {
            var orderId = TryExtractBvcTopUpOrderId(webhook.Content);
            if (orderId != null)
            {
                var resolvedOrderId = await _walletService.FindPendingTopUpOrderIdAsync(orderId);
                if (!string.IsNullOrWhiteSpace(resolvedOrderId))
                {
                    var bvcAmount = (long)(webhook.Amount / 1000m);
                    await _walletService.HandleTopUpWebhookAsync(
                        orderId: resolvedOrderId,
                        gatewayTransactionId: webhook.GatewayTransactionId ?? string.Empty,
                        amountBvc: bvcAmount,
                        status: webhook.Status);
                    return;
                }

                _logger.LogWarning(
                    "SePay webhook BVC top-up order matched but no pending request. OrderId={OrderId}, Amount={Amount}",
                    orderId, webhook.Amount);
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

    /// <summary>
    /// Extract userId hash (8 hex chars) từ SePay BankAPINotify content.
    /// Trả về null nếu content không phải dạng BVC top-up.
    ///
    /// Pattern content SePay gửi về (MoMo prefix + transferContent gốc bị strip '-'):
    ///   "140465213621-BVCTOPUP364BED39-CHUYEN TIEN-..." →
    ///     match "BVCTOPUP" + 8 hex chars → "364BED39".
    /// </summary>
    /// <summary>
    /// W-07: Extract OrderId from transferContent.
    /// New format: "BVC-{18hex}" → after SePay strip dashes: "BVC{18hex}".
    /// Regex captures exactly 18 hex chars following "BVC" prefix.
    /// </summary>
    private static string? TryExtractBvcTopUpOrderId(string content)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"BVCTOPUP([A-F0-9]{18})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
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

            // BUGFIX (subagent audit #6): cancellation webhook when deposit is still Pending
            // → MarkAsRefundedAsync only accepts Paid → throws. Use ExpireAsync for Pending.
            // MarkAsRefundedAsync is for post-payment refunds (Paid → Refunded).
            // Failed/cancelled gateway payment on Pending deposit should mark Expired.
            await _depositService.ExpireAsync(deposit.Id);
            _logger.LogInformation("Booking deposit expired (payment failed/cancelled). DepositId={DepositId}", deposit.Id);
        }
    }

    private async Task ProcessSessionPaymentWebhookAsync(SePayWebhookDto webhook)
    {
        if (string.IsNullOrWhiteSpace(webhook.OrderId))
        {
            _logger.LogWarning("SePay webhook for session payment missing OrderId.");
            return;
        }

        // BUGFIX (subagent audit #3): không fallback sang GetAllUnpaidAsync() linear scan
        // khi SessionId null. Phải lookup qua OrderId hoặc DB index.
        // Trước đây: nếu SessionId null + OrderId lookup fail → quét toàn bộ unpaid sessions
        // (O(N), race risk: 2 webhook cùng lookup có thể pick cùng session).
        // Sau: dùng GetByOrderIdAsync (index-based) hoặc skip xử lý.
        var session = await _activeSessionRepository.GetByIdAsync(webhook.SessionId ?? Guid.Empty);
        if (session == null)
        {
            // Try OrderId lookup via dedicated index (1 query, no scan).
            session = await _activeSessionRepository.GetByOrderIdAsync(webhook.OrderId);
        }

        if (session == null)
        {
            _logger.LogWarning("SePay webhook session payment not matched. OrderId={OrderId}", webhook.OrderId);
            return;
        }

        var normalizedStatus = webhook.Status?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalizedStatus is "success" or "paid")
        {
            // Validate amount BEFORE any state change so we never half-commit.
            if (webhook.Amount != session.TotalAmount)
            {
                _logger.LogWarning(
                    "SePay webhook amount mismatch for session. Expected={Expected}, Received={Received}, SessionId={SessionId}",
                    session.TotalAmount, webhook.Amount, session.Id);
                return;
            }

            // P0 Fix #2: Use atomic status update to prevent race condition (double-payment).
            var updated = await _activeSessionRepository.TryUpdateStatusAsync(
                session.Id,
                GroupSessionStatus.Unpaid,
                GroupSessionStatus.Paid);

            if (!updated)
            {
                _logger.LogWarning(
                    "SePay webhook session status update failed (race condition or already paid). SessionId={SessionId}",
                    session.Id);
                return;
            }

            // Lifecycle cleanup: mark members checked out, release box + table, close lobby.
            await _activeSessionRepository.CompleteSessionPaymentCleanupAsync(session.Id);

            _logger.LogInformation(
                "Session payment completed via SePay. SessionId={SessionId}, Amount={Amount}. Table, board game box and lobby released.",
                session.Id, session.TotalAmount);
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
    /// Trả về RefundDepositResult gồm BookingDeposit (sau update) + số tiền thực tế hoàn cho khách.
    /// </summary>
    public async Task<RefundDepositResult> RefundDepositAsync(Guid depositId, string reason, Guid actorUserId, string actorRole)
    {
        var deposit = await _depositService.GetByIdAsync(depositId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        // C3: Validate cafe ownership for Manager. Admin bypasses.
        // Manager: chỉ được refund deposit thuộc quán do mình quản lý (deposit.CafeManagerId == actorUserId).
        // Admin: xem tất cả.
        if (actorRole == "Manager" && deposit.CafeManagerId != actorUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Payment.NotAuthorizedToViewDeposit);
        }
        else if (actorRole != "Admin" && actorRole != "Manager")
        {
            throw new ForbiddenException(ApiErrorMessages.Payment.NotAuthorizedToViewDeposit);
        }

        if (deposit.Status != BookingDepositStatus.Paid)
        {
            throw new ConflictException(ApiErrorMessages.Payment.RefundInvalidDepositStatus(deposit.Status.ToString()));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BadRequestException(ApiErrorMessages.Payment.RefundReasonRequired);
        }

        // Tính số tiền hoàn dự kiến theo policy TRƯỚC khi chuyển trạng thái.
        var expectedRefund = _depositService.CalculatePartialRefundAmount(deposit);

        BookingDeposit updated;
        if (deposit.RefundPolicy == DepositRefundPolicy.None)
        {
            updated = await _depositService.ForfeitAsync(depositId);
            expectedRefund = 0m;
        }
        else
        {
            updated = await _depositService.MarkAsRefundedAsync(depositId);
        }

        _logger.LogInformation(
            "BookingDeposit refund processed. DepositId={DepositId}, Policy={Policy}, OriginalAmount={Amount}, RefundedAmount={Refunded}, Reason={Reason}",
            updated.Id, updated.RefundPolicy, updated.Amount, expectedRefund, reason);

        return new RefundDepositResult
        {
            Deposit = updated,
            RefundedAmount = expectedRefund
        };
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

    /// <summary>
    /// C4/C5: Verify caller is allowed to operate on the cafe that owns the session.
    /// - Admin: bypass.
    /// - Manager: only the cafe's own manager (cafe.ManagerId == actorUserId).
    /// - CafeStaff: must be linked to the cafe via CafeStaff row.
    /// Throws ForbiddenException otherwise.
    /// </summary>
    private async Task VerifyCafeOperatorAsync(Cafe cafe, Guid actorUserId, string actorRole)
    {
        if (actorRole == "Admin")
        {
            return;
        }

        if (actorRole == "Manager")
        {
            if (cafe.ManagerId != actorUserId)
            {
                throw new ForbiddenException(ApiErrorMessages.Payment.NotAuthorizedToViewDeposit);
            }
            return;
        }

        if (actorRole == "CafeStaff")
        {
            var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafe.Id, actorUserId);
            if (!isStaff)
            {
                throw new ForbiddenException(ApiErrorMessages.Payment.NotAuthorizedToViewDeposit);
            }
            return;
        }

        throw new ForbiddenException(ApiErrorMessages.Payment.NotAuthorizedToViewDeposit);
    }
}
