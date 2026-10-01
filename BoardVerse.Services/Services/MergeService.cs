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
using System.Data;

namespace BoardVerse.Services.Services;

/// <summary>
/// M1 / Option A — Service xử lý refund PER-MEMBER deposit khi member merge sang lobby khác (Exception 4).
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §B4.
///
/// <para>
/// <b>SCOPE QUAN TRỌNG — chỉ PER-MEMBER deposit (BR-22):</b>
/// Service này chỉ chạm vào <c>ActiveSessionMember.DepositId</c> (per-member deposit của member).
/// KHÔNG đụng vào <c>Reservation.DepositAmount</c> (host deposit) — host deposit giờ FOLLOWS merged
/// members qua merge chain (xem <c>LobbyMergeService.ApproveMergeAsync</c>, docs §B3.1).
/// </para>
///
/// <para>Side effects khi merge member (chỉ áp dụng cho per-member deposit):</para>
/// <list type="number">
///   <item>Refund per-member deposit (BookingDeposit.Amount) về <c>member.UserId</c> wallet.</item>
///   <item>Ghi ledger entry <c>DepositRefund_Merge</c> (append-only, immutable).</item>
///   <item>Update member: DepositId=null, DepositAppliedAmount=0, DepositRefundedAt=now,
///         DepositRefundReason="Merged từ Lobby X", DepositRefundLedgerId=ledgerEntry.Id.</item>
///   <item>Insert <c>MemberDepositAuditLog</c> row với action="Refunded_OnMerge".</item>
/// </list>
/// Tất cả trong 1 ambient transaction (Serializable isolation) để đảm bảo atomicity.
///
/// <para>
/// <b>Host deposit KHÔNG qua flow này</b> — xem <c>LobbyMergeService.ApproveMergeAsync</c> Step 11
/// để biết chi tiết cách host deposit được carry over sang target reservation thay vì refund.
/// </para>
/// </remarks>
public class MergeService : IMergeService
{
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly IBookingDepositRepository _depositRepository;
    private readonly IMemberDepositAuditLogRepository _auditLogRepository;
    private readonly IWalletService _walletService;
    private readonly BoardVerseDbContext _db;
    private readonly ILogger<MergeService> _logger;

    public MergeService(
        IActiveSessionRepository activeSessionRepository,
        IBookingDepositRepository depositRepository,
        IMemberDepositAuditLogRepository auditLogRepository,
        IWalletService walletService,
        BoardVerseDbContext db,
        ILogger<MergeService> logger)
    {
        _activeSessionRepository = activeSessionRepository;
        _depositRepository = depositRepository;
        _auditLogRepository = auditLogRepository;
        _walletService = walletService;
        _db = db;
        _logger = logger;
    }

    public async Task<MergeMemberRefundResponseDto> HandleMemberMergeAsync(
        Guid memberId,
        Guid fromLobbyId,
        Guid? toLobbyId,
        Guid staffUserId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var member = await _activeSessionRepository.GetMemberByIdAsync(memberId, ct)
            ?? throw new NotFoundException(
                $"Member '{memberId}' không tồn tại.");

        // ============================================================
        // GUARD: Guest_Slot → không có deposit để refund
        // ============================================================
        if (member.IsGuestSlot)
        {
            _logger.LogInformation(
                "M1 MergeService: Member {MemberId} là Guest_Slot → skip refund deposit.",
                memberId);

            await LogSkipActionAsync(member, "GuestSlot_NoDeposit",
                "Guest slot không có deposit để refund.",
                fromLobbyId, toLobbyId, staffUserId, now, ct);

            return new MergeMemberRefundResponseDto
            {
                MemberId = memberId,
                UserId = null,
                DepositRefunded = false,
                DepositRefundedBvc = 0,
                Action = "GuestSlot_NoDeposit",
                Reason = "Guest_Slot không có UserId/deposit để refund.",
                ProcessedAt = now
            };
        }

        if (!member.UserId.HasValue)
        {
            throw new InvalidOperationException(
                $"Member '{memberId}' không phải Guest_Slot nhưng UserId = null — data inconsistency.");
        }

        // ============================================================
        // IDEMPOTENT: Đã refund rồi → trả về audit log row gốc
        // ============================================================
        if (member.DepositRefundedAt.HasValue)
        {
            _logger.LogWarning(
                "M1 MergeService: Member {MemberId} deposit đã refund tại {RefundedAt} → idempotent skip.",
                memberId, member.DepositRefundedAt);

            var existingAuditLog = (await _auditLogRepository.GetByMemberIdAsync(memberId, take: 1, ct))
                .FirstOrDefault();
            return new MergeMemberRefundResponseDto
            {
                MemberId = memberId,
                UserId = member.UserId,
                DepositRefunded = true,
                DepositRefundedBvc = existingAuditLog?.AmountBvc ?? 0,
                LedgerEntryId = member.DepositRefundLedgerId,
                AuditLogId = existingAuditLog?.Id ?? Guid.Empty,
                Action = "IdempotentReplay",
                Reason = member.DepositRefundReason,
                ProcessedAt = member.DepositRefundedAt.Value
            };
        }

        // ============================================================
        // AMBIENT TRANSACTION: refund + update member + audit log atomic
        // ============================================================
        var ownsTransaction = _db.Database.CurrentTransaction is null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? ownedTx = null;

        if (ownsTransaction)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        }

        try
        {
            long depositRefundedBvc = 0;
            Guid? ledgerEntryId = null;
            string action;
            string reason;

            // ============================================================
            // CASE 1: Member có DepositId + DepositAppliedAmount = 0 → refund merge (Option A)
            // ============================================================
            if (member.DepositId.HasValue && member.DepositAppliedAmount == 0m)
            {
                var deposit = await _depositRepository.GetByIdAsync(member.DepositId.Value, ct);
                if (deposit == null)
                {
                    throw new NotFoundException(
                        $"Deposit '{member.DepositId.Value}' không tồn tại.");
                }

                if (deposit.UserId != member.UserId.Value)
                {
                    throw new InvalidOperationException(
                        $"Deposit '{deposit.Id}' thuộc user '{deposit.UserId}' ≠ member.UserId '{member.UserId.Value}'.");
                }

                // Convert decimal → long (BVC). Round to nearest integer (deposit amount is integer BVC).
                depositRefundedBvc = (long)Math.Round(deposit.Amount, MidpointRounding.AwayFromZero);
                if (depositRefundedBvc <= 0)
                {
                    _logger.LogInformation(
                        "M1 MergeService: Member {MemberId} deposit {DepositId} amount=0 → no refund needed.",
                        memberId, deposit.Id);

                    action = "GuestSlot_NoDeposit"; // reuse label — semantically "no refund"
                    reason = "Deposit amount = 0.";
                }
                else
                {
                    var idempotencyKey =
                        $"refund-merge-{memberId}-{member.DepositId.Value}-{now:o}";

                    var ledgerEntry = await _walletService.RefundMemberDepositOnMergeAsync(
                        userId: member.UserId.Value,
                        amountBvc: depositRefundedBvc,
                        depositId: deposit.Id,
                        idempotencyKey: idempotencyKey,
                        notes: $"Member merge từ Lobby {fromLobbyId} → {toLobbyId?.ToString() ?? "N/A"}");

                    ledgerEntryId = ledgerEntry?.Id;

                    // Update member entity
                    member.DepositId = null;
                    member.DepositAppliedAmount = 0m;
                    member.DepositRefundedAt = now;
                    member.DepositRefundReason = $"Merged từ Lobby {fromLobbyId}";
                    member.DepositRefundLedgerId = ledgerEntryId;
                    member.MergedFromLobbyId = fromLobbyId;
                    member.MergedAt = now;
                    member.UpdatedAt = now;

                    action = "Refunded_OnMerge";
                    reason = $"Member merge từ Lobby {fromLobbyId} → {toLobbyId?.ToString() ?? "N/A"}. " +
                             $"Refund {depositRefundedBvc} BVC về wallet.";
                }
            }
            // ============================================================
            // CASE 2: Member có DepositId + DepositAppliedAmount > 0 → race lost (deposit đã apply)
            // KHÔNG refund — BR-22 forward-compat chỉ refund khi deposit chưa apply.
            // ============================================================
            else if (member.DepositId.HasValue && member.DepositAppliedAmount > 0m)
            {
                _logger.LogWarning(
                    "M1 MergeService: Member {MemberId} has DepositId {DepositId} already applied {Applied} BVC " +
                    "to bill before merge → no refund (race lost).",
                    memberId, member.DepositId, member.DepositAppliedAmount);

                member.DepositId = null;
                member.DepositRefundReason = "DepositConsumed_BeforeMerge";
                member.UpdatedAt = now;

                action = "DepositConsumed_BeforeMerge";
                reason = $"Deposit {member.DepositId} đã apply {member.DepositAppliedAmount} BVC vào bill trước khi merge.";
            }
            // ============================================================
            // CASE 3: Member không có DepositId → không có gì để refund (forward-compat, BR-22 chưa active)
            // ============================================================
            else
            {
                _logger.LogInformation(
                    "M1 MergeService: Member {MemberId} không có DepositId → no refund needed (BR-22 chưa active).",
                    memberId);

                member.MergedFromLobbyId = fromLobbyId;
                member.MergedAt = now;
                member.UpdatedAt = now;

                action = "NoDepositToRefund";
                reason = "Member không có per-member deposit (BR-22 chưa active).";
            }

            // Update member entity qua repository (SaveChangesAsync ở cuối transaction).
            await _activeSessionRepository.UpdateMemberAsync(member);

            // Insert audit log row (idempotent qua IdempotencyKey).
            var idempotencyAuditKey = $"audit-merge-{memberId}-{fromLobbyId}-{now:o}";
            var auditLog = await _auditLogRepository.AddAsync(new MemberDepositAuditLog
            {
                Id = Guid.NewGuid(),
                MemberId = memberId,
                UserId = member.UserId,
                Action = action,
                AmountBvc = depositRefundedBvc,
                DepositId = member.DepositId,
                FromLobbyId = fromLobbyId,
                FromSessionId = member.ActiveSessionId,
                ToLobbyId = toLobbyId,
                ToSessionId = null,
                MergedAt = now,
                Reason = reason,
                IdempotencyKey = idempotencyAuditKey,
                LedgerEntryId = ledgerEntryId,
                CreatedByUserId = staffUserId,
                CreatedAt = now
            }, ct);

            await _db.SaveChangesAsync(ct);

            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(ct);
            }

            _logger.LogInformation(
                "M1 MergeService: HandleMemberMergeAsync completed. MemberId={MemberId}, Action={Action}, " +
                "RefundBvc={Refund}, FromLobby={FromLobby}, ToLobby={ToLobby}",
                memberId, action, depositRefundedBvc, fromLobbyId, toLobbyId);

            return new MergeMemberRefundResponseDto
            {
                MemberId = memberId,
                UserId = member.UserId,
                DepositRefunded = depositRefundedBvc > 0,
                DepositRefundedBvc = depositRefundedBvc,
                LedgerEntryId = ledgerEntryId,
                AuditLogId = auditLog.Id,
                Action = action,
                Reason = reason,
                ProcessedAt = now
            };
        }
        catch
        {
            if (ownedTx != null)
            {
                await ownedTx.RollbackAsync(ct);
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

    /// <summary>
    /// Helper ghi audit log cho các skip action (Guest_Slot, no deposit).
    /// Dùng transaction riêng vì action này không có side-effect quan trọng.
    /// </summary>
    private async Task LogSkipActionAsync(
        ActiveSessionMember member,
        string action,
        string reason,
        Guid fromLobbyId,
        Guid? toLobbyId,
        Guid staffUserId,
        DateTime now,
        CancellationToken ct)
    {
        try
        {
            var idempotencyAuditKey = $"audit-merge-skip-{member.Id}-{fromLobbyId}-{now:o}";
            await _auditLogRepository.AddAsync(new MemberDepositAuditLog
            {
                Id = Guid.NewGuid(),
                MemberId = member.Id,
                UserId = member.UserId,
                Action = action,
                AmountBvc = 0,
                DepositId = member.DepositId,
                FromLobbyId = fromLobbyId,
                FromSessionId = member.ActiveSessionId,
                ToLobbyId = toLobbyId,
                ToSessionId = null,
                MergedAt = now,
                Reason = reason,
                IdempotencyKey = idempotencyAuditKey,
                LedgerEntryId = null,
                CreatedByUserId = staffUserId,
                CreatedAt = now
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "M1 MergeService: Failed to write skip audit log for Member {MemberId}. Continue.",
                member.Id);
        }
    }
}
