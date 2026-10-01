using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// M2 / Case 2 — Member BVC bill payment orchestration.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.1–§C2.12.
/// </summary>
/// <remarks>
/// <para>
/// Phối hợp 3 layers:
/// </para>
/// <list type="number">
///   <item><see cref="IWalletService"/> — ledger + wallet atomicity.</item>
///   <item><see cref="IActiveSessionRepository"/> — session/member persistence.</item>
///   <item><see cref="BoardVerseDbContext"/> — direct DbContext cho MemberPaymentAuditLog + atomic flip.</item>
/// </list>
/// <para>
/// Tất cả mutations wrap trong 1 Serializable transaction (hoặc dùng ambient nếu caller đã wrap).
/// </para>
/// </remarks>
public class WalletSessionPaymentService : IWalletSessionPaymentService
{
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly IWalletService _walletService;
    private readonly BoardVerseDbContext _db;
    private readonly ILogger<WalletSessionPaymentService> _logger;

    public WalletSessionPaymentService(
        IActiveSessionRepository activeSessionRepository,
        IWalletService walletService,
        BoardVerseDbContext db,
        ILogger<WalletSessionPaymentService> logger)
    {
        _activeSessionRepository = activeSessionRepository;
        _walletService = walletService;
        _db = db;
        _logger = logger;
    }

    // ============================================================
    // Task C2.6 — Preview bill
    // ============================================================
    public async Task<MemberBillPreviewDto> GetMemberBillPreviewAsync(
        Guid sessionId,
        Guid memberId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        var session = await _activeSessionRepository.GetByIdWithMembersAsync(sessionId, cancellationToken)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(sessionId));

        var member = session.Members.FirstOrDefault(m => m.Id == memberId)
            ?? throw new NotFoundException($"Không tìm thấy thành viên '{memberId}' trong phiên chơi.");

        // Gap-#1: Authorization check (Player = own member OR host; staff/manager/admin = always).
        EnsureCanAccessMember(member, session, actorUserId, actorRole);

        // BR-14: Guest_Slot không bị charge penalty → set PenaltyAmount = 0 cho preview.
        var effectivePenalty = member.IsGuestSlot ? 0m : member.PenaltyAmount;
        var totalDue = member.Subtotal + effectivePenalty - member.DepositAppliedAmount;

        // AllowBvcPenalty: Guest_Slot không cho phép trả penalty (chỉ member mới có).
        // Member có UserId mới thực sự có wallet BVC.
        var allowBvcPenalty = !member.IsGuestSlot && member.UserId.HasValue;

        return new MemberBillPreviewDto
        {
            SessionId = sessionId,
            MemberId = memberId,
            UserId = member.UserId,
            DisplayName = member.IsGuestSlot
                ? (member.GuestDisplayName ?? "Khách vô danh")
                : (member.User?.Username ?? "Unknown"),
            IsGuestSlot = member.IsGuestSlot,
            Subtotal = member.Subtotal,
            PenaltyAmount = effectivePenalty,
            DepositAppliedAmount = member.DepositAppliedAmount,
            TotalDue = totalDue,
            AllowBvcPenalty = allowBvcPenalty,
            PaymentStatus = member.PaymentStatus
        };
    }

    // ============================================================
    // Task C2.7 — Pay member bill
    // ============================================================
    public async Task<MemberBillPaymentResponseDto> PayMemberBillAsync(
        Guid sessionId,
        Guid memberId,
        MemberBillPaymentRequestDto request,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new BadRequestException("Request không được rỗng.");
        }

        if (request.BvcAmount < 0)
        {
            throw new BadRequestException("BvcAmount phải lớn hơn hoặc bằng 0.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        var session = await _activeSessionRepository.GetByIdWithMembersAsync(sessionId, cancellationToken)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ActiveSessionNotFound(sessionId));

        // Validate session status — chỉ Unpaid mới cho pay.
        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException(
                ApiErrorMessages.Wallet.SessionNotUnpaidForMemberBill);
        }

        var member = session.Members.FirstOrDefault(m => m.Id == memberId)
            ?? throw new NotFoundException($"Không tìm thấy thành viên '{memberId}' trong phiên chơi.");

        // ============================================================
        // Gap-#1: Authorization check (Player = own member OR host; staff/manager/admin = always).
        // Phải đặt TRƯỚC mọi mutation để tránh IDOR.
        // ============================================================
        EnsureCanAccessMember(member, session, actorUserId, actorRole);

        // Validate member chưa paid (chỉ NotPaid mới cho pay).
        if (member.PaymentStatus != MemberPaymentStatus.NotPaid)
        {
            throw new ConflictException(ApiErrorMessages.Wallet.MemberAlreadyPaid);
        }

        // ============================================================
        // C2.4 — Distinct UserId check (Gap #7 multi-account)
        // ============================================================
        var duplicateUserIds = session.Members
            .Where(m => m.UserId.HasValue)
            .GroupBy(m => m.UserId!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateUserIds.Count > 0)
        {
            throw new ConflictException(
                ApiErrorMessages.Wallet.DuplicateUserIdInSession(
                    string.Join(", ", duplicateUserIds.Select(u => u.ToString()))));
        }

        // ============================================================
        // Tính totalDue
        // ============================================================
        // BR-14: Guest_Slot penalty = 0.
        var effectivePenalty = member.IsGuestSlot ? 0m : member.PenaltyAmount;
        var totalDue = member.Subtotal + effectivePenalty - member.DepositAppliedAmount;

        // Validate overpayment (C2.2 Gap #6) — bỏ qua khi BvcAmount == 0 (chỉ cash, không BVC).
        if (request.BvcAmount > 0 && request.BvcAmount > (long)totalDue)
        {
            throw new BadRequestException(
                ApiErrorMessages.Wallet.BvcBillOverpayment(request.BvcAmount, totalDue));
        }

        // ============================================================
        // AMBIENT TRANSACTION
        // ============================================================
        var ownsTransaction = _db.Database.CurrentTransaction is null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? ownedTx = null;

        if (ownsTransaction)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
        }

        try
        {
            var now = DateTime.UtcNow;
            var bvcAmount = request.BvcAmount;

            // ============================================================
            // Idempotency check (C2.5 Gap #17): check audit log đã tồn tại với key này chưa.
            // Gap-#3: Nếu đã tồn tại nhưng cho member KHÁC → 409 (idempotency key là global).
            // Nếu cùng member + cùng amount → return existing record (replay).
            // Nếu cùng member + khác amount → 409.
            // ============================================================
            var existingAudit = await _db.MemberPaymentAuditLogs
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.IdempotencyKey == request.IdempotencyKey, cancellationToken);

            if (existingAudit != null)
            {
                // Gap-#3: Key đã được dùng cho member khác → 409 (không lộ memberId cũ).
                if (existingAudit.MemberId != memberId)
                {
                    throw new ConflictException(ApiErrorMessages.Wallet.IdempotencyKeyAlreadyUsed);
                }

                if (existingAudit.AmountBvc != bvcAmount)
                {
                    throw new ConflictException(
                        $"Idempotency key đã được dùng với AmountBvc {existingAudit.AmountBvc} ≠ {bvcAmount}.");
                }

                // Idempotent replay: trả lại response cũ.
                _logger.LogInformation(
                    "M2 PayMemberBillAsync idempotent replay. IdempotencyKey={Key}, MemberId={MemberId}, Amount={Amount}",
                    request.IdempotencyKey, memberId, bvcAmount);

                if (ownedTx != null)
                {
                    await ownedTx.CommitAsync(cancellationToken);
                }

                return new MemberBillPaymentResponseDto
                {
                    MemberId = memberId,
                    BvcAmount = existingAudit.AmountBvc,
                    CashRemainder = existingAudit.AmountCash,
                    Status = member.PaymentStatus,
                    PaymentMethod = existingAudit.PaymentMethod,
                    PaidAt = existingAudit.CreatedAt,
                    LedgerEntryId = existingAudit.WalletTxnId ?? Guid.Empty,
                    AuditLogId = existingAudit.Id
                };
            }

            // ============================================================
            // Nếu bvcAmount == 0 → member trả cash 100% (status = PaidCash, không touch wallet).
            // Branch đặc biệt: KHÔNG gọi DirectDebitForBillAsync.
            // ============================================================
            if (bvcAmount == 0)
            {
                member.PaymentStatus = MemberPaymentStatus.PaidCash;
                member.PaymentMethod = "CASH";
                member.PaidAt = now;
                member.PaidBvcAmount = 0;
                member.PaidCashRemainder = totalDue;
                member.TransactionId = null;

                await _activeSessionRepository.UpdateMemberAsync(member);

                // Insert audit log cho cash (replicate để consolidate report).
                var cashAudit = new MemberPaymentAuditLog
                {
                    Id = Guid.NewGuid(),
                    MemberId = memberId,
                    UserId = member.UserId,
                    PaymentMethod = "Cash",
                    AmountBvc = 0,
                    AmountCash = totalDue,
                    WalletTxnId = null,
                    IdempotencyKey = request.IdempotencyKey,
                    CreatedByStaffId = IsStaffRole(actorRole) ? actorUserId : null,
                    CreatedAt = now
                };
                _db.MemberPaymentAuditLogs.Add(cashAudit);
                await _db.SaveChangesAsync(cancellationToken);

                if (ownedTx != null)
                {
                    await ownedTx.CommitAsync(cancellationToken);
                }

                // C2.10 all-paid trigger.
                await TryFinalizeSessionAsync(sessionId, cancellationToken);

                _logger.LogInformation(
                    "M2 PayMemberBillAsync cash-only. MemberId={MemberId}, TotalDue={TotalDue}, AuditLogId={AuditLogId}",
                    memberId, totalDue, cashAudit.Id);

                return new MemberBillPaymentResponseDto
                {
                    MemberId = memberId,
                    BvcAmount = 0,
                    CashRemainder = totalDue,
                    Status = MemberPaymentStatus.PaidCash,
                    PaymentMethod = "CASH",
                    PaidAt = now,
                    LedgerEntryId = Guid.Empty,
                    AuditLogId = cashAudit.Id
                };
            }

            // ============================================================
            // BvcAmount > 0 → gọi wallet service debit
            // ============================================================

            // Validate member có UserId (Guest_Slot không có wallet → không thể trả BVC).
            if (!member.UserId.HasValue)
            {
                throw new ConflictException(
                    ApiErrorMessages.Session.GuestCannotPayViaApp);
            }

            // Idempotency key cho ledger (phân biệt với audit log key để trace).
            var ledgerIdempotencyKey = $"wallet-bill-debit-{request.IdempotencyKey}";

            var ledgerEntry = await _walletService.DirectDebitForBillAsync(
                userId: member.UserId.Value,
                amountBvc: bvcAmount,
                memberId: memberId,
                billId: sessionId,
                idempotencyKey: ledgerIdempotencyKey,
                cancellationToken);

            // Xác định status + cash remainder.
            var isFullBvc = bvcAmount == (long)totalDue;
            var cashRemainder = totalDue - bvcAmount;
            MemberPaymentStatus newStatus;
            string paymentMethod;

            if (isFullBvc)
            {
                newStatus = MemberPaymentStatus.PaidBvc;
                paymentMethod = "Bvc";
            }
            else
            {
                newStatus = MemberPaymentStatus.PartialBvc;
                paymentMethod = "BvcPartial";
            }

            // Update member.
            member.PaymentStatus = newStatus;
            member.PaymentMethod = paymentMethod;
            member.PaidAt = now;
            member.PaidBvcAmount = bvcAmount;
            member.PaidCashRemainder = cashRemainder;
            member.TransactionId = ledgerEntry.Id;

            await _activeSessionRepository.UpdateMemberAsync(member);

            // Insert audit log.
            var auditLog = new MemberPaymentAuditLog
            {
                Id = Guid.NewGuid(),
                MemberId = memberId,
                UserId = member.UserId,
                PaymentMethod = paymentMethod,
                AmountBvc = bvcAmount,
                AmountCash = cashRemainder,
                WalletTxnId = ledgerEntry.Id,
                IdempotencyKey = request.IdempotencyKey,
                CreatedByStaffId = IsStaffRole(actorRole) ? actorUserId : null,
                CreatedAt = now
            };
            _db.MemberPaymentAuditLogs.Add(auditLog);
            await _db.SaveChangesAsync(cancellationToken);

            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "M2 PayMemberBillAsync completed. MemberId={MemberId}, BvcAmount={Bvc}, Status={Status}, " +
                "CashRemainder={Cash}, LedgerEntryId={LedgerId}, AuditLogId={AuditLogId}",
                memberId, bvcAmount, newStatus, cashRemainder, ledgerEntry.Id, auditLog.Id);

            // C2.10 all-paid trigger.
            await TryFinalizeSessionAsync(sessionId, cancellationToken);

            return new MemberBillPaymentResponseDto
            {
                MemberId = memberId,
                BvcAmount = bvcAmount,
                CashRemainder = cashRemainder,
                Status = newStatus,
                PaymentMethod = paymentMethod,
                PaidAt = now,
                LedgerEntryId = ledgerEntry.Id,
                AuditLogId = auditLog.Id
            };
        }
        catch
        {
            if (ownedTx != null)
            {
                await ownedTx.RollbackAsync(cancellationToken);
            }
            throw;
        }
        finally
        {
            if (ownedTx != null)
            {
                await ownedTx.DisposeAsync();
            }
        }
    }

    // ============================================================
    // Task C2.8 — Refund bill
    // ============================================================
    public async Task<MemberBillRefundResponseDto> RefundMemberBillAsync(
        Guid memberPaymentAuditLogId,
        MemberBillRefundRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new BadRequestException("Request không được rỗng.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BadRequestException("Lý do refund là bắt buộc.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        // ============================================================
        // AMBIENT TRANSACTION
        // ============================================================
        var ownsTransaction = _db.Database.CurrentTransaction is null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? ownedTx = null;

        if (ownsTransaction)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
        }

        try
        {
            var now = DateTime.UtcNow;

            // Lookup audit log + member trong cùng transaction.
            var auditLog = await _db.MemberPaymentAuditLogs
                .FirstOrDefaultAsync(a => a.Id == memberPaymentAuditLogId, cancellationToken)
                ?? throw new NotFoundException(
                    $"Không tìm thấy MemberPaymentAuditLog '{memberPaymentAuditLogId}'.");

            // Idempotent guard: nếu đã refund → trả về response cũ.
            if (auditLog.RefundedAt.HasValue)
            {
                _logger.LogInformation(
                    "M2 RefundMemberBillAsync idempotent replay. AuditLogId={AuditLogId}, AlreadyRefundedAt={RefundedAt}",
                    memberPaymentAuditLogId, auditLog.RefundedAt);

                if (ownedTx != null)
                {
                    await ownedTx.CommitAsync(cancellationToken);
                }

                return new MemberBillRefundResponseDto
                {
                    MemberPaymentAuditLogId = auditLog.Id,
                    MemberId = auditLog.MemberId,
                    RefundedBvc = auditLog.AmountBvc,
                    RefundedAt = auditLog.RefundedAt.Value,
                    RefundReason = auditLog.RefundReason,
                    RefundLedgerEntryId = auditLog.RefundLedgerEntryId ?? Guid.Empty
                };
            }

            // Validate audit log đã được pay (AmountBvc > 0).
            if (auditLog.AmountBvc <= 0 || !auditLog.UserId.HasValue)
            {
                throw new ConflictException(
                    $"MemberPaymentAuditLog '{memberPaymentAuditLogId}' không phải BVC payment (AmountBvc={auditLog.AmountBvc}, UserId={auditLog.UserId?.ToString() ?? "null"}). Không thể refund.");
            }

            // Lookup member.
            var member = await _db.ActiveSessionMembers
                .FirstOrDefaultAsync(m => m.Id == auditLog.MemberId, cancellationToken)
                ?? throw new NotFoundException(
                    $"Không tìm thấy thành viên '{auditLog.MemberId}'.");

            // Idempotency key cho ledger.
            var ledgerIdempotencyKey = $"wallet-bill-refund-{request.IdempotencyKey}";

            // Call wallet service để refund (atomic wallet + ledger).
            var refundEntry = await _walletService.RefundMemberBillAsync(
                userId: auditLog.UserId.Value,
                amountBvc: auditLog.AmountBvc,
                memberId: auditLog.MemberId,
                idempotencyKey: ledgerIdempotencyKey,
                cancellationToken);

            // Update audit log.
            auditLog.RefundedAt = now;
            auditLog.RefundReason = request.Reason;
            auditLog.RefundLedgerEntryId = refundEntry.Id;

            // Update member: status = RefundedBvc + tracking fields.
            member.BvcRefundedAt = now;
            member.BvcRefundReason = request.Reason;
            member.PaymentStatus = MemberPaymentStatus.RefundedBvc;
            member.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);

            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(cancellationToken);
            }

            _logger.LogWarning(
                "M2 RefundMemberBillAsync completed. AuditLogId={AuditLogId}, MemberId={MemberId}, " +
                "RefundedBvc={Bvc}, RefundLedgerEntryId={LedgerId}, ActorUserId={ActorUserId}, Reason={Reason}",
                auditLog.Id, auditLog.MemberId, auditLog.AmountBvc, refundEntry.Id, actorUserId, request.Reason);

            return new MemberBillRefundResponseDto
            {
                MemberPaymentAuditLogId = auditLog.Id,
                MemberId = auditLog.MemberId,
                RefundedBvc = auditLog.AmountBvc,
                RefundedAt = now,
                RefundReason = request.Reason,
                RefundLedgerEntryId = refundEntry.Id
            };
        }
        catch
        {
            if (ownedTx != null)
            {
                await ownedTx.RollbackAsync(cancellationToken);
            }
            throw;
        }
        finally
        {
            if (ownedTx != null)
            {
                await ownedTx.DisposeAsync();
            }
        }
    }

    // ============================================================
    // Task C2.10 — All-paid trigger (atomic flip session.Status = Paid)
    // ============================================================
    /// <summary>
    /// Check tất cả members đã paid → atomic flip session.Status = Paid.
    /// Pattern copied từ <see cref="SplitBillService.CheckAndFinalizeSessionAsync"/>.
    /// </summary>
    private async Task TryFinalizeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        // Refresh session + members (post-commit, cần đọc DB mới nhất).
        var session = await _activeSessionRepository.GetByIdWithMembersAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return;
        }

        // Idempotent: nếu session đã Paid → skip.
        if (session.Status == GroupSessionStatus.Paid)
        {
            return;
        }

        // Check all members paid: PaymentStatus NOT in (NotPaid, PartialBvc).
        // Member với Status = Finished (early leave) bị bỏ qua — giống SplitBillService pattern.
        var allPaid = session.Members.All(m =>
            m.PaymentStatus != MemberPaymentStatus.NotPaid
            && m.PaymentStatus != MemberPaymentStatus.PartialBvc
            || m.Status == IndividualSessionStatus.Finished);

        if (!allPaid)
        {
            return;
        }

        // Atomic flip (Postgres path — InMemory provider fallback cho tests).
        if (_db.Database.ProviderName?.Contains("InMemory") == true)
        {
            var sessionEntity = await _db.ActiveSessions
                .FirstOrDefaultAsync(s => s.Id == session.Id, cancellationToken);

            if (sessionEntity == null || sessionEntity.Status == GroupSessionStatus.Paid)
            {
                _logger.LogInformation(
                    "M2 TryFinalizeSessionAsync: Session {SessionId} already finalized.",
                    sessionId);
                return;
            }

            sessionEntity.Status = GroupSessionStatus.Paid;
            sessionEntity.PaidAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var rowsUpdated = await _db.ActiveSessions
                .Where(s => s.Id == session.Id && s.Status != GroupSessionStatus.Paid)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Status, GroupSessionStatus.Paid)
                    .SetProperty(s => s.PaidAt, DateTime.UtcNow),
                    cancellationToken);

            if (rowsUpdated == 0)
            {
                _logger.LogInformation(
                    "M2 TryFinalizeSessionAsync: Session {SessionId} already finalized by concurrent request.",
                    sessionId);
                return;
            }
        }

        _logger.LogInformation(
            "M2 TryFinalizeSessionAsync: All members paid. Session {SessionId} → Paid.",
            sessionId);

        // Cleanup: release table/box (idempotent, an toàn gọi nhiều lần).
        try
        {
            await _activeSessionRepository.ReleaseMembersAndCloseLobbyAsync(sessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "M2 TryFinalizeSessionAsync: ReleaseMembersAndCloseLobby failed for SessionId={SessionId}. " +
                "Payment already committed; cleanup will retry via AutoReleaseExpiredSessionsJob.",
                sessionId);
        }

        try
        {
            await _activeSessionRepository.ReleaseSessionTableAndBoxAsync(sessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "M2 TryFinalizeSessionAsync: ReleaseSessionTableAndBox failed for SessionId={SessionId}. " +
                "Payment already committed; cleanup will retry via AutoReleaseExpiredSessionsJob.",
                sessionId);
        }
    }

    private static bool IsStaffRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        return role.Equals("CafeStaff", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Manager", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================
    // Gap-#1: Authorization check cho member bill endpoints.
    // ============================================================
    /// <summary>
    /// Kiểm tra actor có quyền truy cập bill của member hay không.
    /// <list type="bullet">
    ///   <item>Admin / Manager / CafeStaff: luôn pass (đã được controller-level filter).</item>
    ///   <item>Player: chỉ được nếu <c>member.UserId == actorUserId</c> HOẶC là host của session.</item>
    /// </list>
    /// </summary>
    /// <exception cref="ForbiddenException">Actor không có quyền.</exception>
    private static void EnsureCanAccessMember(
        ActiveSessionMember member,
        ActiveSession session,
        Guid actorUserId,
        string actorRole)
    {
        // Staff/manager/admin: bypass (đã được controller-level [Authorize(Roles=...)] filter).
        if (IsStaffRole(actorRole))
        {
            return;
        }

        // Player: phải là chính member này hoặc là host của session.
        var isOwnMember = member.UserId.HasValue && member.UserId.Value == actorUserId;
        var isHost = session.HostId == actorUserId;

        if (!isOwnMember && !isHost)
        {
            throw new ForbiddenException(ApiErrorMessages.Wallet.MemberBillAccessDenied);
        }
    }
}