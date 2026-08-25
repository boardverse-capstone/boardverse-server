using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Split Bill Service — thanh toán per-member trong group session.
/// Tất cả fixes áp dụng 2026-08-25 (Gap #1-13 + webhook security fixes).
/// </summary>
public class SplitBillService : ISplitBillService
{
    private readonly IActiveSessionRepository _sessionRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly IPaymentGatewayService _paymentGateway;
    private readonly BoardVerseDbContext _dbContext;
    private readonly ILogger<SplitBillService> _logger;

    // Fix #2: Idempotency key repository
    private readonly IPaymentWebhookAuditRepository _webhookAuditRepository;

    public SplitBillService(
        IActiveSessionRepository sessionRepository,
        ITransactionRepository transactionRepository,
        ICafeRepository cafeRepository,
        IPaymentGatewayService paymentGateway,
        BoardVerseDbContext dbContext,
        ILogger<SplitBillService> logger,
        IPaymentWebhookAuditRepository webhookAuditRepository)
    {
        _sessionRepository = sessionRepository;
        _transactionRepository = transactionRepository;
        _cafeRepository = cafeRepository;
        _paymentGateway = paymentGateway;
        _dbContext = dbContext;
        _logger = logger;
        _webhookAuditRepository = webhookAuditRepository;
    }

    public async Task<SessionPaymentStatusDto> GetSessionPaymentStatusAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdWithMembersAsync(sessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(sessionId));

        var members = session.Members
            .Where(m => m.Status != IndividualSessionStatus.Finished)
            .ToList();

        var memberDtos = members.Select(m => new MemberPaymentStatusDto
        {
            MemberId = m.Id,
            DisplayName = m.IsGuestSlot ? m.GuestDisplayName ?? "Khách" : m.User?.Username ?? "Unknown",
            TotalAmount = m.TotalAmount,
            // Fix #2: AmountPaid = TotalAmount khi đã trả, không phải 0 khi đã trả
            AmountPaid = m.PaymentStatus != MemberPaymentStatus.NotPaid ? m.TotalAmount : 0,
            Status = m.PaymentStatus,
            PaymentMethod = m.PaymentMethod
        }).ToList();

        var totalPaid = members
            .Where(m => m.PaymentStatus != MemberPaymentStatus.NotPaid)
            .Sum(m => m.TotalAmount);

        return new SessionPaymentStatusDto
        {
            SessionId = sessionId,
            TotalAmount = session.TotalAmount,
            TotalPaid = totalPaid,
            TotalRemaining = session.TotalAmount - totalPaid,
            Members = memberDtos
        };
    }

    public async Task<List<MemberPaymentResponseDto>> PayMembersAsync(
        Guid sessionId,
        PayMemberRequestDto request,
        Guid staffId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        // Fix #4: Staff permission validated FIRST — before any member data query
        var session = await _sessionRepository.GetByIdWithMembersAsync(sessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(sessionId));

        await ValidateStaffPermissionAsync(session.CafeId, staffId, actorRole, cancellationToken);

        // Fix #5: BadRequestException instead of ArgumentException
        var validMethods = new[] { "CASH", "QR_CODE" };
        if (request.MemberIds == null || request.MemberIds.Count == 0)
        {
            throw new BadRequestException("Danh sách thành viên không được rỗng.");
        }
        if (!validMethods.Contains(request.PaymentMethod?.ToUpperInvariant() ?? ""))
        {
            throw new BadRequestException(ApiErrorMessages.Payment.InvalidPaymentMethod(request.PaymentMethod ?? "null"));
        }

        // Validate session status
        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException(ApiErrorMessages.Payment.SessionNotUnpaid(session.Status.ToString()));
        }

        // Fix #6: Tách 2 trường hợp rõ ràng — not found vs already paid
        var allMemberIds = session.Members
            .Where(m => m.Status != IndividualSessionStatus.Finished)
            .ToList();

        var foundMembers = allMemberIds
            .Where(m => request.MemberIds.Contains(m.Id))
            .ToList();

        if (foundMembers.Count != request.MemberIds.Count)
        {
            var missingIds = request.MemberIds.Except(allMemberIds.Select(m => m.Id)).ToList();
            if (missingIds.Any())
            {
                throw new NotFoundException($"Không tìm thấy thành viên với ID: {missingIds.First()}");
            }
            // Nếu tất cả tìm thấy nhưng có member đã thanh toán
            var alreadyPaid = foundMembers.FirstOrDefault(m => m.PaymentStatus != MemberPaymentStatus.NotPaid);
            if (alreadyPaid != null)
            {
                throw new ConflictException($"Thành viên '{alreadyPaid.GuestDisplayName ?? alreadyPaid.User?.Username}' đã thanh toán rồi.");
            }
        }

        // Check if any member already paid
        var alreadyPaidCheck = foundMembers.FirstOrDefault(m => m.PaymentStatus != MemberPaymentStatus.NotPaid);
        if (alreadyPaidCheck != null)
        {
            throw new ConflictException($"Thành viên '{alreadyPaidCheck.GuestDisplayName ?? alreadyPaidCheck.User?.Username}' đã thanh toán rồi.");
        }

        var responses = new List<MemberPaymentResponseDto>();

        if (request.PaymentMethod!.Equals("CASH", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var member in foundMembers)
            {
                var response = await ConfirmMemberCashInternalAsync(
                    session, member, session.CafeId, staffId, actorRole, request.Notes, cancellationToken);
                responses.Add(response);
            }
        }
        else
        {
            foreach (var member in foundMembers)
            {
                var response = await CreateMemberQrInternalAsync(
                    session, member, staffId, cancellationToken);
                responses.Add(response);
            }
        }

        return responses;
    }

    public async Task<MemberPaymentResponseDto> CreateMemberQrAsync(
        Guid sessionId,
        Guid memberId,
        Guid staffId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdWithMembersAsync(sessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(sessionId));

        var member = session.Members.FirstOrDefault(m => m.Id == memberId)
            ?? throw new NotFoundException($"Không tìm thấy thành viên '{memberId}'.");

        await ValidateStaffPermissionAsync(session.CafeId, staffId, actorRole, cancellationToken);

        return await CreateMemberQrInternalAsync(session, member, staffId, cancellationToken);
    }

    // Fix #13: ConfirmMemberCashAsync kept in interface for backward compat
    // but internal validation order improved (auth first, then amount)
    public async Task<MemberPaymentResponseDto> ConfirmMemberCashAsync(
        Guid sessionId,
        Guid memberId,
        decimal amount,
        Guid staffId,
        string actorRole,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdWithMembersAsync(sessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(sessionId));

        var member = session.Members.FirstOrDefault(m => m.Id == memberId)
            ?? throw new NotFoundException($"Không tìm thấy thành viên '{memberId}'.");

        await ValidateStaffPermissionAsync(session.CafeId, staffId, actorRole, cancellationToken);

        // Fix #8: Amount comparison with tolerance (1 VND)
        if (!AmountEquals(amount, member.TotalAmount))
        {
            throw new ConflictException(
                ApiErrorMessages.Payment.ManualConfirmAmountMismatch(member.TotalAmount, amount));
        }

        return await ConfirmMemberCashInternalAsync(
            session, member, session.CafeId, staffId, actorRole, notes, cancellationToken);
    }

    // Fix #1, #2, #5, #6, #7: Complete webhook security and reliability fixes
    public async Task ProcessMemberQrWebhookAsync(
        MemberPaymentWebhookDto webhook,
        CancellationToken cancellationToken = default)
    {
        // Fix #1: Audit logging - record every webhook invocation
        var webhookId = Guid.NewGuid();
        _logger.LogInformation(
            "[Webhook Audit] Processing member payment webhook. WebhookId={WebhookId}, OrderId={OrderId}, MemberId={MemberId}, Amount={Amount}, Status={Status}",
            webhookId, webhook.OrderId, webhook.MemberId, webhook.Amount, webhook.Status);

        // Fix #2: Durable idempotency - check if we've already processed this gateway transaction
        if (!string.IsNullOrEmpty(webhook.GatewayTransactionId))
        {
            var existingAudit = await _webhookAuditRepository.GetByGatewayTransactionIdAsync(
                webhook.GatewayTransactionId, cancellationToken);
            if (existingAudit != null)
            {
                _logger.LogInformation(
                    "[Webhook Audit] Duplicate webhook detected. WebhookId={WebhookId}, GatewayTxnId={GatewayTxnId}, PreviousAuditId={AuditId}, Skipping.",
                    webhookId, webhook.GatewayTransactionId, existingAudit.Id);
                return;
            }
        }

        // Fix #1: Resolve memberId with multiple fallback strategies
        var memberId = webhook.MemberId;

        if (memberId == Guid.Empty && !string.IsNullOrEmpty(webhook.OrderId))
        {
            try
            {
                memberId = ParseMemberIdFromOrderId(webhook.OrderId);
            }
            catch (ArgumentException ex)
            {
                // Fix #7: Handle multiple OrderId formats gracefully
                _logger.LogWarning(
                    "[Webhook Audit] Could not parse memberId from OrderId. WebhookId={WebhookId}, OrderId={OrderId}, Error={Error}",
                    webhookId, webhook.OrderId, ex.Message);
                // Don't throw - try to look up by gateway transaction ID instead
                if (!string.IsNullOrEmpty(webhook.GatewayTransactionId))
                {
                    // Try to find member by looking for a pending payment with this gateway txn
                    var foundMember = await FindMemberByGatewayTransactionIdAsync(webhook.GatewayTransactionId, cancellationToken);
                    if (foundMember != null)
                    {
                        memberId = foundMember.Id;
                        _logger.LogInformation(
                            "[Webhook Audit] Found member via gateway transaction. WebhookId={WebhookId}, MemberId={MemberId}",
                            webhookId, memberId);
                    }
                }

                if (memberId == Guid.Empty)
                {
                    // Fix #6: Return 200 with warning instead of 404 to prevent SePay from stopping retries
                    _logger.LogWarning(
                        "[Webhook Audit] Member not found. WebhookId={WebhookId}, OrderId={OrderId}, GatewayTxnId={GatewayTxnId}",
                        webhookId, webhook.OrderId, webhook.GatewayTransactionId);
                    // Record audit for failed lookup
                    await RecordWebhookAuditAsync(webhookId, webhook, memberId, success: false,
                        "Member not found for OrderId or GatewayTxnId", cancellationToken);
                    return; // Return 200 OK to prevent SePay from stopping retries
                }
            }
        }

        if (memberId == Guid.Empty)
        {
            _logger.LogWarning(
                "[Webhook Audit] MemberId still empty after parsing. WebhookId={WebhookId}",
                webhookId);
            await RecordWebhookAuditAsync(webhookId, webhook, memberId, success: false,
                "MemberId is empty", cancellationToken);
            return;
        }

        var session = await _sessionRepository.GetByMemberIdWithSessionAsync(memberId, cancellationToken);
        if (session == null)
        {
            _logger.LogWarning(
                "[Webhook Audit] Session not found for member. WebhookId={WebhookId}, MemberId={MemberId}",
                webhookId, memberId);
            await RecordWebhookAuditAsync(webhookId, webhook, memberId, success: false,
                $"Session not found for member {memberId}", cancellationToken);
            return; // Fix #6: Return 200 instead of throwing
        }

        var member = session.Members.FirstOrDefault(m => m.Id == memberId);
        if (member == null)
        {
            _logger.LogWarning(
                "[Webhook Audit] Member not found in session. WebhookId={WebhookId}, MemberId={MemberId}, SessionId={SessionId}",
                webhookId, memberId, session.Id);
            await RecordWebhookAuditAsync(webhookId, webhook, memberId, success: false,
                $"Member {memberId} not found in session {session.Id}", cancellationToken);
            return; // Fix #6: Return 200 instead of throwing
        }

        // Fix #5: Log when session already paid
        if (member.PaymentStatus != MemberPaymentStatus.NotPaid)
        {
            _logger.LogInformation(
                "[Webhook Audit] Member already paid (status={Status}). WebhookId={WebhookId}, MemberId={MemberId}",
                member.PaymentStatus, webhookId, memberId);
            await RecordWebhookAuditAsync(webhookId, webhook, memberId, success: true,
                $"Already paid (status: {member.PaymentStatus})", cancellationToken);
            return;
        }

        // Handle failed/cancelled payments
        if (webhook.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
            webhook.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) ||
            webhook.Status.Equals("canceled", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "[Webhook Audit] Payment failed/cancelled. WebhookId={WebhookId}, MemberId={MemberId}, Status={Status}",
                webhookId, memberId, webhook.Status);
            await RecordWebhookAuditAsync(webhookId, webhook, memberId, success: true,
                $"Payment {webhook.Status}", cancellationToken);
            return;
        }

        // Process successful payment
        if (webhook.Status.Equals("success", StringComparison.OrdinalIgnoreCase) ||
            webhook.Status.Equals("paid", StringComparison.OrdinalIgnoreCase))
        {
            // Fix #8: Amount comparison with tolerance
            if (!AmountEquals(webhook.Amount, member.TotalAmount))
            {
                _logger.LogWarning(
                    "[Webhook Audit] Amount mismatch. WebhookId={WebhookId}, MemberId={MemberId}, Expected={Expected}, Got={Actual}",
                    webhookId, memberId, member.TotalAmount, webhook.Amount);
                await RecordWebhookAuditAsync(webhookId, webhook, memberId, success: false,
                    $"Amount mismatch: expected {member.TotalAmount}, got {webhook.Amount}", cancellationToken);
                // Fix #6: Return 200 with warning, don't throw ConflictException to webhook caller
                return;
            }

            // GAP #2 FIX: Create Transaction record for QR payment
            var txId = await CreateTransactionForQrPaymentAsync(
                session, member, webhook, cancellationToken);

            var paidAt = webhook.PaidAt ?? DateTime.UtcNow;

            await UpdateMemberPaymentStatusAsync(
                session, member, MemberPaymentStatus.PaidQr, "QR_CODE",
                txId, paidAt, webhook.OrderId, staffIdForWebhook: Guid.Empty, cancellationToken);

            _logger.LogInformation(
                "[Webhook Audit] Payment processed successfully. WebhookId={WebhookId}, MemberId={MemberId}, Amount={Amount}",
                webhookId, memberId, webhook.Amount);
            await RecordWebhookAuditAsync(webhookId, webhook, memberId, success: true,
                $"Payment successful: {webhook.Amount}", cancellationToken);
        }
    }

    /// <summary>
    /// GAP #2 FIX: Create Transaction record when QR payment succeeds via webhook.
    /// This ensures proper audit trail for per-member payments.
    /// </summary>
    private async Task<Guid?> CreateTransactionForQrPaymentAsync(
        ActiveSession session,
        ActiveSessionMember member,
        MemberPaymentWebhookDto webhook,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;

            // Parse GatewayTransactionId từ webhook hoặc tạo mới
            var gatewayTxnId = webhook.GatewayTransactionId ?? Guid.NewGuid().ToString();

            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = member.UserId ?? Guid.Empty,
                CafeId = session.CafeId,
                Amount = webhook.Amount,
                Currency = "VND",
                Gateway = webhook.Gateway ?? "SePay",
                GatewayTransactionId = gatewayTxnId,
                GatewayResponseCode = "SUCCESS",
                GatewayResponseMessage = $"QR payment thanh toán thành công. OrderId: {webhook.OrderId}",
                Status = TransactionStatus.Succeeded,
                Type = TransactionType.GameRental,
                Direction = TransactionDirection.In,
                Notes = $"QR payment for member {member.Id} ({member.GuestDisplayName ?? member.User?.Username ?? "Unknown"}). " +
                        $"SessionId: {session.Id}. Reference: {webhook.ReferenceCode ?? "N/A"}",
                CreatedAt = now,
                CompletedAt = now
            };

            await _transactionRepository.AddAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "[GAP #2] Created Transaction for QR payment. TransactionId={TransactionId}, MemberId={MemberId}, Amount={Amount}",
                transaction.Id, member.Id, webhook.Amount);

            return transaction.Id;
        }
        catch (Exception ex)
        {
            // Non-critical: transaction creation fail không block payment
            _logger.LogWarning(ex,
                "[GAP #2] Failed to create Transaction for QR payment. MemberId={MemberId}, Amount={Amount}. " +
                "Payment vẫn thành công.",
                member.Id, webhook.Amount);
            return null;
        }
    }

    // Fix #7: Helper method to find member by gateway transaction ID
    private async Task<ActiveSessionMember?> FindMemberByGatewayTransactionIdAsync(
        string gatewayTransactionId, CancellationToken cancellationToken)
    {
        // Look for a member payment record with this gateway transaction
        var memberPayment = await _dbContext.MemberPayments
            .Where(mp => mp.TransactionId.ToString() == gatewayTransactionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (memberPayment != null)
        {
            return await _dbContext.ActiveSessionMembers
                .Include(m => m.User)
                .Where(m => m.Id == memberPayment.MemberId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    // Fix #1: Helper method to record webhook audit
    private async Task RecordWebhookAuditAsync(
        Guid webhookId,
        MemberPaymentWebhookDto webhook,
        Guid memberId,
        bool success,
        string notes,
        CancellationToken cancellationToken)
    {
        try
        {
            var audit = new PaymentWebhookAudit
            {
                Id = webhookId,
                Endpoint = "/api/payments/sepay/webhook/member-payment",
                Payload = System.Text.Json.JsonSerializer.Serialize(webhook),
                GatewayTransactionId = webhook.GatewayTransactionId,
                OrderId = webhook.OrderId,
                Amount = webhook.Amount,
                Status = webhook.Status,
                ProcessedAt = DateTime.UtcNow,
                IsSuccess = success,
                ErrorMessage = success ? null : notes,
                Notes = notes,
                ProcessedBy = "SplitBillService",
                MemberId = memberId != Guid.Empty ? memberId : null
            };

            await _webhookAuditRepository.AddAsync(audit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record webhook audit. WebhookId={WebhookId}", webhookId);
            // Don't throw - audit failure shouldn't break the webhook flow
        }
    }

    public async Task<MemberPaymentResponseDto> ConfirmMemberQrAsync(
        Guid sessionId,
        Guid memberId,
        Guid staffId,
        string actorRole,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdWithMembersAsync(sessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(sessionId));

        var member = session.Members.FirstOrDefault(m => m.Id == memberId)
            ?? throw new NotFoundException($"Không tìm thấy thành viên '{memberId}'.");

        if (member.PaymentStatus != MemberPaymentStatus.NotPaid)
        {
            throw new ConflictException("Thành viên này đã thanh toán rồi.");
        }

        // Fix #7: ConfirmMemberQr chỉ áp dụng khi member đã chọn QR trước đó
        // Nếu PaymentMethod == CASH thì không cho confirm QR
        if (member.PaymentMethod == "CASH")
        {
            throw new ConflictException(
                "Thành viên này đã chọn thanh toán tiền mặt. Không thể xác nhận QR.");
        }

        await ValidateStaffPermissionAsync(session.CafeId, staffId, actorRole, cancellationToken);

        return await UpdateMemberPaymentStatusAsync(
            session, member, MemberPaymentStatus.PaidQr, "QR_CODE",
            transactionId: null, paidAt: DateTime.UtcNow,
            orderId: null, staffIdForWebhook: staffId, cancellationToken);
    }

    /// <summary>
    /// Fix #11: Tạo lại QR thanh toán cho member khi QR cũ bị lỗi/hết hạn.
    /// Chỉ áp dụng khi member đã chọn paymentMethod=QR_CODE và chưa trả.
    /// Không cần regenerate nếu SePay đã có QR vĩnh viễn — chỉ dùng khi gateway fail.
    /// </summary>
    public async Task<MemberPaymentResponseDto> RegenerateMemberQrAsync(
        Guid sessionId,
        Guid memberId,
        Guid staffId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdWithMembersAsync(sessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(sessionId));

        var member = session.Members.FirstOrDefault(m => m.Id == memberId)
            ?? throw new NotFoundException($"Không tìm thấy thành viên '{memberId}'.");

        await ValidateStaffPermissionAsync(session.CafeId, staffId, actorRole, cancellationToken);

        // Chỉ cho phép regenerate khi member đã chọn QR và chưa trả
        if (member.PaymentStatus != MemberPaymentStatus.NotPaid)
        {
            throw new ConflictException("Thành viên đã thanh toán rồi.");
        }
        if (member.PaymentMethod != "QR_CODE")
        {
            throw new ConflictException(
                $"Thành viên đang chọn '{member.PaymentMethod ?? "chưa chọn"}', không phải QR. " +
                "Dùng '/pay-member' để chọn phương thức thanh toán.");
        }

        return await CreateMemberQrInternalAsync(session, member, staffId, cancellationToken);
    }

    #region Private Methods

    private async Task<MemberPaymentResponseDto> CreateMemberQrInternalAsync(
        ActiveSession session,
        ActiveSessionMember member,
        Guid staffId,
        CancellationToken cancellationToken)
    {
        var cafe = await _cafeRepository.GetActiveByIdAsync(session.CafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(session.CafeId));

        // Fix #2: Dùng full Guid hex (32 ký tự) — không dùng 8 ký tự nữa
        // Format: BV-MEMBER-{fullGuid}
        var orderId = $"BV-MEMBER-{member.Id:N}";

        var gatewayRequest = new PaymentGatewayRequest
        {
            OrderId = orderId,
            Amount = member.TotalAmount,
            Description = $"Thanh toan cho {member.GuestDisplayName ?? member.User?.Username ?? "Khach"}",
            BankCode = cafe.SePayBankCode ?? throw new InvalidOperationException(
                $"Cafe '{cafe.Name}' chưa cấu hình SePay bank code."),
            AccountNumber = cafe.SePayAccountNumber ?? throw new InvalidOperationException(
                $"Cafe '{cafe.Name}' chưa cấu hình SePay account number."),
            AccountName = cafe.Name,
            Metadata = new Dictionary<string, string?>
            {
                ["sessionId"] = session.Id.ToString(),
                ["memberId"] = member.Id.ToString(),
                ["staffId"] = staffId.ToString()
            }
        };

        var result = await _paymentGateway.CreatePaymentAsync(gatewayRequest, cancellationToken);

        // Fix #2: TransferContent KHỚP với OrderId (cùng format)
        _logger.LogInformation(
            "Created member QR. OrderId={OrderId}, MemberId={MemberId}, Amount={Amount}",
            orderId, member.Id, member.TotalAmount);

        return new MemberPaymentResponseDto
        {
            MemberId = member.Id,
            DisplayName = member.GuestDisplayName ?? member.User?.Username ?? "Unknown",
            AmountDue = member.TotalAmount,
            AmountPaid = 0,
            PaymentMethod = "QR_CODE",
            Status = MemberPaymentStatus.NotPaid,
            OrderId = orderId, // Fix #2: dùng full orderId thật
            QrImageUrl = result.QrImageUrl,
            PaymentUrl = result.PaymentUrl,
            TransferContent = orderId // Fix #2: dùng cùng OrderId thay vì shortGuid
        };
    }

    private async Task<MemberPaymentResponseDto> ConfirmMemberCashInternalAsync(
        ActiveSession session,
        ActiveSessionMember member,
        Guid cafeId,
        Guid staffId,
        string actorRole,
        string? notes,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // GAP #5 FIX: GatewayTransactionId = transaction.Id thay vì session.Id
        // transaction.Id là GUID thực của bản ghi, dùng để lookup/reconcile
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = member.UserId ?? Guid.Empty,
            CafeId = cafeId,
            Amount = member.TotalAmount,
            Currency = "VND",
            Gateway = "MANUAL",
            GatewayTransactionId = Guid.NewGuid().ToString(), // GAP #5 FIX: unique transaction ref
            GatewayResponseCode = "CASH_PAYMENT",
            GatewayResponseMessage = "Thanh toán tiền mặt",
            Status = TransactionStatus.Succeeded,
            Type = TransactionType.GameRental,
            Direction = TransactionDirection.In,
            Notes = $"Cash payment for member {member.Id} by Staff: {staffId} (Role={actorRole}). {notes ?? ""}",
            CreatedAt = now,
            CompletedAt = now
        };

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        var response = await UpdateMemberPaymentStatusAsync(
            session, member, MemberPaymentStatus.PaidCash, "CASH",
            transaction.Id, now, orderId: null, staffIdForWebhook: staffId, cancellationToken);

        response.AmountPaid = member.TotalAmount;

        return response;
    }

    /// <summary>
    /// Fix #3: Atomic flip — dùng ExecuteUpdateAsync để tránh race condition
    /// khi CASH + QR webhook cùng đến cho cùng member.
    /// Trả về true nếu flip thành công, false nếu member đã được set trước đó.
    /// InMemory provider không hỗ trợ ExecuteUpdateAsync → fallback sang direct update.
    /// </summary>
    private async Task<bool> TryAtomicFlipMemberPaymentStatusAsync(
        ActiveSessionMember member,
        MemberPaymentStatus expectedStatus,
        MemberPaymentStatus newStatus,
        string paymentMethod,
        Guid? transactionId,
        CancellationToken cancellationToken)
    {
        // InMemory provider doesn't support ExecuteUpdateAsync — fallback
        if (_dbContext.Database.ProviderName?.Contains("InMemory") == true)
        {
            var current = await _dbContext.ActiveSessionMembers
                .FirstOrDefaultAsync(m => m.Id == member.Id, cancellationToken);

            if (current == null || current.PaymentStatus != expectedStatus)
                return false;

            current.PaymentStatus = newStatus;
            current.PaymentMethod = paymentMethod;
            current.TransactionId = transactionId;
            current.PaidAt = DateTime.UtcNow;
            current.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Real database: atomic ExecuteUpdateAsync
        var rowsAffected = await _dbContext.ActiveSessionMembers
            .Where(m => m.Id == member.Id && m.PaymentStatus == expectedStatus)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.PaymentStatus, newStatus)
                .SetProperty(m => m.PaymentMethod, paymentMethod)
                .SetProperty(m => m.TransactionId, transactionId)
                .SetProperty(m => m.PaidAt, DateTime.UtcNow)
                .SetProperty(m => m.UpdatedAt, DateTime.UtcNow),
                cancellationToken);

        return rowsAffected > 0;
    }

    private async Task<MemberPaymentResponseDto> UpdateMemberPaymentStatusAsync(
        ActiveSession session,
        ActiveSessionMember member,
        MemberPaymentStatus status,
        string paymentMethod,
        Guid? transactionId,
        DateTime paidAt,
        string? orderId,
        Guid staffIdForWebhook,
        CancellationToken cancellationToken)
    {
        // Fix #3: Atomic flip trước khi tạo audit record
        var flipped = await TryAtomicFlipMemberPaymentStatusAsync(
            member, MemberPaymentStatus.NotPaid, status, paymentMethod, transactionId, cancellationToken);

        if (!flipped)
        {
            _logger.LogWarning(
                "Atomic flip failed for Member {MemberId}. Status already changed. " +
                "This is likely a race condition between CASH and QR.",
                member.Id);
            throw new ConflictException(
                $"Thành viên '{member.GuestDisplayName ?? member.User?.Username ?? "Unknown"}' " +
                "đã được thanh toán bởi một thao tác khác. Vui lòng tải lại trang.");
        }

        // Fix #9: StaffId = staffId parameter, không phải transaction.UserId
        // transaction.UserId là player, không phải staff
        var actualStaffId = staffIdForWebhook != Guid.Empty
            ? staffIdForWebhook
            : (transactionId.HasValue ? staffIdForWebhook : Guid.Empty);

        // Create MemberPayment audit record
        // Fix #2: Lưu full OrderId (không regenerate lại)
        var memberPayment = new MemberPayment
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = session.Id,
            MemberId = member.Id,
            Amount = member.TotalAmount,
            PaymentMethod = paymentMethod,
            OrderId = orderId, // Fix #2: dùng OrderId đã tạo, không regenerate
            TransactionId = transactionId,
            StaffId = staffIdForWebhook, // Fix #9: dùng staffId thật
            CreatedAt = paidAt
        };

        _dbContext.MemberPayments.Add(memberPayment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Member payment updated (atomic). MemberId={MemberId}, Status={Status}, " +
            "Amount={Amount}, OrderId={OrderId}",
            member.Id, status, member.TotalAmount, orderId ?? "N/A");

        await CheckAndFinalizeSessionAsync(session, cancellationToken);

        return new MemberPaymentResponseDto
        {
            MemberId = member.Id,
            DisplayName = member.GuestDisplayName ?? member.User?.Username ?? "Unknown",
            AmountDue = member.TotalAmount,
            AmountPaid = status != MemberPaymentStatus.NotPaid ? member.TotalAmount : 0,
            PaymentMethod = paymentMethod,
            Status = status,
            PaidAt = paidAt,
            OrderId = orderId
        };
    }

    private async Task CheckAndFinalizeSessionAsync(
        ActiveSession session,
        CancellationToken cancellationToken)
    {
        var updatedSession = await _sessionRepository.GetByIdWithMembersAsync(session.Id);
        if (updatedSession == null) return;

        // Fix #3: Kiểm tra idempotency — nếu session đã Paid thì không cần finalize lại
        if (updatedSession.Status == GroupSessionStatus.Paid)
        {
            return;
        }

        var allPaid = updatedSession.Members
            .All(m => m.PaymentStatus != MemberPaymentStatus.NotPaid ||
                      m.Status == IndividualSessionStatus.Finished);

        if (allPaid)
        {
            // Atomic flip để tránh race condition khi nhiều request cùng trigger finalize
            // InMemory provider doesn't support ExecuteUpdateAsync → fallback
            if (_dbContext.Database.ProviderName?.Contains("InMemory") == true)
            {
                var sessionEntity = await _dbContext.ActiveSessions
                    .FirstOrDefaultAsync(s => s.Id == session.Id, cancellationToken);

                if (sessionEntity == null || sessionEntity.Status == GroupSessionStatus.Paid)
                {
                    _logger.LogInformation(
                        "Session {SessionId} already finalized by concurrent request.",
                        session.Id);
                    return;
                }

                sessionEntity.Status = GroupSessionStatus.Paid;
                sessionEntity.PaidAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var rowsUpdated = await _dbContext.ActiveSessions
                    .Where(s => s.Id == session.Id && s.Status != GroupSessionStatus.Paid)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Status, GroupSessionStatus.Paid)
                        .SetProperty(s => s.PaidAt, DateTime.UtcNow),
                        cancellationToken);

                if (rowsUpdated == 0)
                {
                    // Session đã được finalize bởi thread khác — bỏ qua
                    _logger.LogInformation(
                        "Session {SessionId} already finalized by concurrent request.",
                        session.Id);
                    return;
                }
            }

            _logger.LogInformation(
                "All members paid. Session {SessionId} finalized.",
                session.Id);

            try
            {
                await _sessionRepository.ReleaseMembersAndCloseLobbyAsync(session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error releasing members for Session {SessionId}. Payment already completed.",
                    session.Id);
            }

            try
            {
                await _sessionRepository.ReleaseSessionTableAndBoxAsync(session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error releasing table/box for Session {SessionId}. Payment already completed.",
                    session.Id);
            }
        }
    }

    private async Task ValidateStaffPermissionAsync(
        Guid cafeId,
        Guid staffId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var isOwner = cafe.ManagerId == staffId;
        var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, staffId);

        if (!isOwner && !isStaff)
        {
            throw new ForbiddenException(
                ApiErrorMessages.Payment.ManualConfirmNotAuthorizedForCafe(cafeId));
        }
    }

    // Fix #2: Parse full Guid từ OrderId — BV-MEMBER-{full32charGuid}
    private static Guid ParseMemberIdFromOrderId(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("OrderId rỗng");

        // Try full format: BV-MEMBER-{N-format}
        if (orderId.StartsWith("BV-MEMBER-", StringComparison.OrdinalIgnoreCase))
        {
            var guidPart = orderId["BV-MEMBER-".Length..];
            if (Guid.TryParse(guidPart, out var memberId))
            {
                return memberId;
            }
        }

        throw new ArgumentException($"Không parse được MemberId từ OrderId: {orderId}");
    }

    // Fix #8: Decimal comparison với tolerance 1 VND
    private static bool AmountEquals(decimal a, decimal b, decimal tolerance = 1m)
        => Math.Abs(a - b) <= tolerance;

    #endregion
}
