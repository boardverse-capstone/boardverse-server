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
/// M2/C2.16 — Force-close service implementation (Gap #33).
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16.
/// (2026-10-01)
/// </summary>
/// <remarks>
/// Pattern copied từ <see cref="WalletSessionPaymentService"/>: ambient transaction,
/// atomic flip, idempotent audit log insert.
/// </remarks>
public class ForceCloseService : IForceCloseService
{
    private readonly BoardVerseDbContext _db;
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly ILogger<ForceCloseService> _logger;

    public ForceCloseService(
        BoardVerseDbContext db,
        IActiveSessionRepository activeSessionRepository,
        ILogger<ForceCloseService> logger)
    {
        _db = db;
        _activeSessionRepository = activeSessionRepository;
        _logger = logger;
    }

    public async Task<ForceCloseResponseDto> ForceCloseWithUnpaidAsync(
        Guid sessionId,
        ForceCloseRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new BadRequestException("Request không được rỗng.");
        }

        // ===== Lookup session + members =====
        var session = await _activeSessionRepository.GetByIdWithMembersAsync(sessionId, cancellationToken)
            ?? throw new NotFoundException(ApiErrorMessages.Session.SessionNotFoundById(sessionId));

        // ===== Validate session status (phải Unpaid) =====
        if (session.Status != GroupSessionStatus.Unpaid)
        {
            throw new ConflictException(
                ApiErrorMessages.Session.ForceCloseSessionNotUnpaid(session.Status.ToString()));
        }

        // ===== Tìm unpaid members =====
        // Member unpaid = PaymentStatus = NotPaid VÀ Status = Playing/Finished (không NoShow).
        var unpaidMembers = session.Members
            .Where(m => m.PaymentStatus == MemberPaymentStatus.NotPaid)
            .ToList();

        if (unpaidMembers.Count == 0)
        {
            throw new ConflictException(ApiErrorMessages.Session.ForceCloseNoUnpaidMembers);
        }

        // ===== Validate handling-specific =====
        if (request.UnpaidMemberHandling == "CompensationByHost")
        {
            // Host phải đã paid.
            var hostMember = session.Members.FirstOrDefault(m => m.IsHost);
            if (hostMember == null || hostMember.PaymentStatus == MemberPaymentStatus.NotPaid)
            {
                throw new ConflictException(ApiErrorMessages.Session.ForceCloseHostNotPaid);
            }
        }

        // ===== AMBIENT TRANSACTION (Serializable) =====
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
            var handledMembers = new List<UnpaidMemberDto>();
            var unpaidMemberIds = unpaidMembers.Select(m => m.Id).ToList();

            foreach (var member in unpaidMembers)
            {
                var displayName = member.IsGuestSlot
                    ? member.GuestDisplayName ?? "Khách vô danh"
                    : member.User?.Username ?? $"User_{member.UserId?.ToString()[..8] ?? "unknown"}";

                // Tính amount còn nợ: Subtotal + Penalty - DepositAppliedAmount.
                var effectivePenalty = member.IsGuestSlot ? 0m : member.PenaltyAmount;
                var amountOwed = member.Subtotal + effectivePenalty - member.DepositAppliedAmount;

                switch (request.UnpaidMemberHandling)
                {
                    case "MarkNoShow":
                        member.Status = IndividualSessionStatus.NoShow;
                        member.NoShowAt = now;
                        member.NoShowReason = $"ForceClose_MarkNoShow: {request.Reason}";
                        // Gap-#5: MarkNoShow = bill bị forfeit, member KHÔNG trả tiền.
                        // Set PaymentStatus = PaidByHost (không phải NotPaid) để all-paid check
                        // không block finalization — semantically "settled by forfeit".
                        member.PaymentStatus = MemberPaymentStatus.PaidByHost;
                        member.PaidByHostAt = now;
                        member.PaidByHostUserId = null; // No-show — không có host cover.
                        handledMembers.Add(new UnpaidMemberDto
                        {
                            MemberId = member.Id,
                            DisplayName = displayName,
                            Amount = amountOwed,
                            Handling = "MarkedNoShow",
                            DebtId = null
                        });
                        break;

                    case "MarkAsDebt":
                        member.Status = IndividualSessionStatus.Finished;
                        var debt = new MemberDebt
                        {
                            Id = Guid.NewGuid(),
                            MemberId = member.Id,
                            UserId = member.UserId,
                            SessionId = sessionId,
                            AmountBvc = 0,
                            AmountCash = amountOwed,
                            Status = DebtStatus.Pending,
                            Reason = $"ForceClose_MarkAsDebt: {request.Reason}",
                            CreatedAt = now
                        };
                        _db.MemberDebts.Add(debt);
                        handledMembers.Add(new UnpaidMemberDto
                        {
                            MemberId = member.Id,
                            DisplayName = displayName,
                            Amount = amountOwed,
                            Handling = "MarkedAsDebt",
                            DebtId = debt.Id
                        });
                        break;

                    case "CompensationByHost":
                        // Guest_Slot không thể được cover (Guest không có liability giống Player).
                        if (member.IsGuestSlot)
                        {
                            throw new ConflictException(
                                ApiErrorMessages.Session.ForceCloseGuestNotCovered(displayName));
                        }

                        member.PaymentStatus = MemberPaymentStatus.PaidByHost;
                        member.PaidByHostAt = now;
                        member.PaidByHostUserId = session.HostId;
                        // KHÔNG đổi member.Status (giữ Playing/Finished) — bill đã settled bởi host.
                        handledMembers.Add(new UnpaidMemberDto
                        {
                            MemberId = member.Id,
                            DisplayName = displayName,
                            Amount = amountOwed,
                            Handling = "CoveredByHost",
                            DebtId = null
                        });
                        break;

                    default:
                        throw new ConflictException(ApiErrorMessages.Session.ForceCloseInvalidHandling);
                }

                member.UpdatedAt = now;
            }

            // ===== Save changes =====
            await _db.SaveChangesAsync(cancellationToken);

            // ===== Determine session outcome =====
            // Re-query để check status mới nhất sau khi update members.
            var stillUnpaidCount = await _db.ActiveSessionMembers
                .Where(m => m.ActiveSessionId == sessionId
                    && m.PaymentStatus == MemberPaymentStatus.NotPaid)
                .CountAsync(cancellationToken);

            GroupSessionStatus finalStatus;
            bool sessionClosedAfter;
            bool isInMemory = _db.Database.ProviderName?.Contains("InMemory") == true;

            if (stillUnpaidCount == 0)
            {
                // Tất cả members đã xử lý → atomic flip session.Status = Paid.
                if (isInMemory)
                {
                    var sessionEntity = await _db.ActiveSessions
                        .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
                    if (sessionEntity != null && sessionEntity.Status == GroupSessionStatus.Unpaid)
                    {
                        sessionEntity.Status = GroupSessionStatus.Paid;
                        sessionEntity.PaidAt = now;
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                }
                else
                {
                    var rowsUpdated = await _db.ActiveSessions
                        .Where(s => s.Id == sessionId && s.Status == GroupSessionStatus.Unpaid)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(s => s.Status, GroupSessionStatus.Paid)
                            .SetProperty(s => s.PaidAt, now),
                            cancellationToken);

                    if (rowsUpdated == 0)
                    {
                        _logger.LogInformation(
                            "ForceCloseService: Session {SessionId} already finalized by concurrent request.",
                            sessionId);
                    }
                }

                finalStatus = GroupSessionStatus.Paid;
                sessionClosedAfter = true;

                // Release table/box (best-effort).
                try
                {
                    await _activeSessionRepository.ReleaseSessionTableAndBoxAsync(sessionId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "ForceCloseService: ReleaseSessionTableAndBox failed for SessionId={SessionId}.",
                        sessionId);
                }
            }
            else
            {
                // Vẫn còn unpaid.
                if (!request.AllowLatePayment)
                {
                    // User từ chối late payment mà vẫn còn unpaid → throw.
                    throw new ConflictException(
                        ApiErrorMessages.Session.ForceCloseAllowLatePaymentRequired);
                }

                // AllowLatePayment = true → flip sang UnpaidForced.
                if (isInMemory)
                {
                    var sessionEntity = await _db.ActiveSessions
                        .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
                    if (sessionEntity != null && sessionEntity.Status == GroupSessionStatus.Unpaid)
                    {
                        sessionEntity.Status = GroupSessionStatus.UnpaidForced;
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                }
                else
                {
                    await _db.ActiveSessions
                        .Where(s => s.Id == sessionId && s.Status == GroupSessionStatus.Unpaid)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(s => s.Status, GroupSessionStatus.UnpaidForced),
                            cancellationToken);
                }

                finalStatus = GroupSessionStatus.UnpaidForced;
                sessionClosedAfter = false;
            }

            // ===== Insert ForceCloseAuditLog =====
            var auditLog = new ForceCloseAuditLog
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                TriggeredByUserId = actorUserId,
                UnpaidHandling = request.UnpaidMemberHandling,
                Reason = request.Reason,
                CreatedAt = now,
                UnpaidMemberIds = unpaidMemberIds,
                SessionClosedAfter = sessionClosedAfter
            };
            _db.ForceCloseAuditLogs.Add(auditLog);
            await _db.SaveChangesAsync(cancellationToken);

            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(cancellationToken);
            }

            _logger.LogWarning(
                "M2 ForceCloseService completed. SessionId={SessionId}, TriggeredByUserId={UserId}, " +
                "Handling={Handling}, UnpaidCount={UnpaidCount}, FinalStatus={Status}, AuditLogId={AuditId}",
                sessionId, actorUserId, request.UnpaidMemberHandling,
                unpaidMembers.Count, finalStatus, auditLog.Id);

            return new ForceCloseResponseDto
            {
                SessionId = sessionId,
                Status = finalStatus.ToString(),
                ForceClosedAt = now,
                UnpaidHandling = request.UnpaidMemberHandling,
                UnpaidMemberCount = unpaidMembers.Count,
                UnpaidMembers = handledMembers,
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
}