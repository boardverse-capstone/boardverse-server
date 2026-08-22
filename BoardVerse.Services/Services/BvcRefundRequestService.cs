using System.Text.Json;
using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// BVC Refund Request service — player gửi yêu cầu hoàn, admin duyệt/từ chối.
/// BR-RISK-05: mọi admin resolve action ghi PlayerActionHistory.
/// BR § III.3: ledger append-only — admin approve tạo entry AdminCredit mới.
/// BR § III.6: lifecycle Pending → Approved/Rejected/Cancelled.
/// BR § XVII.1: idempotent theo IdempotencyKey.
/// </summary>
public class BvcRefundRequestService : IBvcRefundRequestService
{
    private const int MinReasonLength = 20;

    private readonly IBvcRefundRequestRepository _refundRequestRepository;
    private readonly IBvcLedgerEntryRepository _ledgerRepository;
    private readonly IUserManagementRepository _userRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly ILogger<BvcRefundRequestService> _logger;
    private readonly BoardVerseDbContext _db;

    public BvcRefundRequestService(
        IBvcRefundRequestRepository refundRequestRepository,
        IBvcLedgerEntryRepository ledgerRepository,
        IUserManagementRepository userRepository,
        IWalletRepository walletRepository,
        ILogger<BvcRefundRequestService> logger,
        BoardVerseDbContext db)
    {
        _refundRequestRepository = refundRequestRepository;
        _ledgerRepository = ledgerRepository;
        _userRepository = userRepository;
        _walletRepository = walletRepository;
        _logger = logger;
        _db = db;
    }

    public async Task<RefundRequestResponseDto> CreateAsync(
        Guid userId,
        CreateRefundRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        if (request.RequestedAmountBvc <= 0)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.RefundRequestInvalidAmount);
        }

        if (string.IsNullOrWhiteSpace(request.PlayerReason)
            || request.PlayerReason.Trim().Length < MinReasonLength)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.RefundRequestReasonTooShort);
        }

        // Idempotency: cùng key + cùng user → trả về request cũ.
        var existing = await _refundRequestRepository.GetByIdempotencyKeyAsync(
            idempotencyKey, cancellationToken);
        if (existing != null)
        {
            if (existing.UserId != userId)
            {
                throw new ConflictException(ApiErrorMessages.Reservation.IdempotencyKeyConflict);
            }

            _logger.LogInformation(
                "Refund request idempotent hit. UserId={UserId}, Key={Key}, Status={Status}",
                userId, idempotencyKey, existing.Status);

            return MapToDto(existing);
        }

        // Validate ledger entry tồn tại + thuộc user.
        var ledgerEntry = await _ledgerRepository.GetByIdAsync(request.RelatedLedgerEntryId);
        if (ledgerEntry == null)
        {
            throw new NotFoundException(ApiErrorMessages.Wallet.RefundRequestLedgerEntryNotFound);
        }

        if (ledgerEntry.UserId != userId)
        {
            throw new ForbiddenException(ApiErrorMessages.Wallet.RefundRequestLedgerEntryNotOwned);
        }

        var now = DateTime.UtcNow;
        var refundRequest = new BvcRefundRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RelatedLedgerEntryId = request.RelatedLedgerEntryId,
            RequestedAmountBvc = request.RequestedAmountBvc,
            PlayerReason = request.PlayerReason.Trim(),
            IdempotencyKey = idempotencyKey,
            Status = RefundRequestStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _refundRequestRepository.AddAsync(refundRequest, cancellationToken);
        await _refundRequestRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Refund request created. RequestId={RequestId}, UserId={UserId}, LedgerEntryId={LedgerEntryId}, RequestedBvc={RequestedBvc}, IdempotencyKey={Key}",
            refundRequest.Id, userId, request.RelatedLedgerEntryId, request.RequestedAmountBvc, idempotencyKey);

        return MapToDto(refundRequest);
    }

    public async Task<RefundRequestPageDto> GetMyRequestsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await _refundRequestRepository.GetByUserIdPagedAsync(
            userId, page, pageSize, cancellationToken);

        return new RefundRequestPageDto
        {
            Items = items.Select(r => MapToDto(r, includeEmail: false)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task CancelAsync(
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.RefundRequestNotFound(requestId));
        }

        // GAP-R6-WAL-03 Fix: lock row refund request để chặn race vs admin ResolveAsync.
        // Trước đây: player CancelAsync + admin ResolveAsync gần nhau → cả 2 pass status check,
        // cả 2 SaveChanges → ambiguous state + có thể double-credit nếu admin approve.
        // Giờ: Serializable transaction + FOR UPDATE lock → 1 process đợi, check lại status sau lock.
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        var request = await _refundRequestRepository.GetByIdForUpdateAsync(requestId, cancellationToken);
        if (request == null)
        {
            throw new NotFoundException(ApiErrorMessages.Wallet.RefundRequestNotFound(requestId));
        }

        if (request.UserId != userId)
        {
            throw new ForbiddenException(ApiErrorMessages.Wallet.RefundRequestNotOwned);
        }

        if (request.Status != RefundRequestStatus.Pending)
        {
            throw new ConflictException(ApiErrorMessages.Wallet.RefundRequestNotPending);
        }

        request.Status = RefundRequestStatus.Cancelled;
        request.UpdatedAt = DateTime.UtcNow;
        await _refundRequestRepository.UpdateAsync(request);
        await _refundRequestRepository.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Refund request cancelled by user. RequestId={RequestId}, UserId={UserId}",
            requestId, userId);
    }

    // ===== Admin =====

    public async Task<RefundRequestPageDto> GetPagedAsync(
        RefundRequestStatus? statusFilter,
        Guid? userIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await _refundRequestRepository.GetPagedAsync(
            statusFilter, userIdFilter, page, pageSize, cancellationToken);

        var ledgerIds = items.Select(i => i.RelatedLedgerEntryId).Distinct().ToList();
        var ledgerMap = await _db.BvcLedgerEntries
            .Where(l => ledgerIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        return new RefundRequestPageDto
        {
            Items = items.Select(r =>
            {
                var dto = MapToDto(r, includeEmail: true);
                if (ledgerMap.TryGetValue(r.RelatedLedgerEntryId, out var ledger))
                {
                    dto.RelatedLedgerEntryType = ledger.Type;
                    dto.RelatedLedgerEntryAmount = ledger.Amount;
                }
                return dto;
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<RefundRequestResponseDto?> GetByIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await _refundRequestRepository.GetByIdWithLedgerEntryAsync(requestId, cancellationToken);
        if (request == null) return null;

        var dto = MapToDto(request, includeEmail: true);

        var ledger = await _ledgerRepository.GetByIdAsync(request.RelatedLedgerEntryId);
        if (ledger != null)
        {
            dto.RelatedLedgerEntryType = ledger.Type;
            dto.RelatedLedgerEntryAmount = ledger.Amount;
        }

        return dto;
    }

    public async Task<RefundRequestResponseDto> ResolveAsync(
        Guid requestId,
        ResolveRefundRequestDto request,
        Guid adminUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.IdempotencyKeyRequired);
        }

        if (string.IsNullOrWhiteSpace(request.AdminNote) || request.AdminNote.Trim().Length < 5)
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.RefundRequestAdminNoteRequired);
        }

        if (request.Decision == RefundDecision.Approve
            && (!request.ApprovedAmountBvc.HasValue || request.ApprovedAmountBvc.Value <= 0))
        {
            throw new BadRequestException(ApiErrorMessages.Wallet.RefundRequestApproveAmountInvalid);
        }

        // Đảm bảo admin tồn tại.
        var admin = await _userRepository.GetByIdAsync(adminUserId)
            ?? throw new NotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(adminUserId));

        // GAP-R6-WAL-02 Fix: wrap resolve flow với serialization-failure retry.
        // 2 admin click "Approve" gần nhau (cùng idempotencyKey) → cả 2 transaction Serializable
        // cùng pass lock check ban đầu, 1 commit thành công, transaction kia fail với
        // Postgres SQLSTATE 40001 (serialization_failure) hoặc 40P01 (deadlock_detected).
        // Không có retry → user thấy 500 error và double-credit có thể xảy ra.
        // Anti-flake loop retry 3 lần với exponential backoff (50ms, 100ms, 150ms).
        const int MaxRetries = 3;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await ResolveCoreAsync(
                    requestId, request, adminUserId, admin, idempotencyKey, cancellationToken);
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "ResolveAsync serialization failure attempt {Attempt}/{Max}. RequestId={RequestId}, AdminId={AdminId}. Retrying...",
                    attempt, MaxRetries, requestId, adminUserId);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
            catch (Exception ex) when (
                ex.InnerException is Npgsql.PostgresException pg
                && (pg.SqlState == "40001" || pg.SqlState == "40P01")
                && attempt < MaxRetries)
            {
                _logger.LogWarning(
                    "ResolveAsync postgres serialization/deadlock attempt {Attempt}/{Max}. RequestId={RequestId}, SqlState={SqlState}. Retrying...",
                    attempt, MaxRetries, requestId, pg.SqlState);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }

        // Unreachable — loop either returns or throws on final attempt.
        return await ResolveCoreAsync(
            requestId, request, adminUserId, admin, idempotencyKey, cancellationToken);
    }

    /// <summary>
    /// Core resolve logic (private) — extracted để <see cref="ResolveAsync"/> retry on serialization failure.
    /// </summary>
    private async Task<RefundRequestResponseDto> ResolveCoreAsync(
        Guid requestId,
        ResolveRefundRequestDto request,
        Guid adminUserId,
        User admin,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {

        var ownsTransaction = _db.Database.CurrentTransaction is null;
        IDbContextTransaction? ownedTx = null;

        if (ownsTransaction)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
        }

        try
        {
            // Lock refund request chống race admin xử lý 2 lần.
            var refundRequest = await _refundRequestRepository.GetByIdForUpdateAsync(requestId, cancellationToken);
            if (refundRequest == null)
            {
                throw new NotFoundException(ApiErrorMessages.Wallet.RefundRequestNotFound(requestId));
            }

            if (refundRequest.Status != RefundRequestStatus.Pending)
            {
                throw new ConflictException(ApiErrorMessages.Wallet.RefundRequestNotPending);
            }

            // Idempotency: ledger key theo (requestId, adminId) + caller key.
            var ledgerKey = $"refund:{requestId}:admin:{adminUserId}:{idempotencyKey}";
            var existingLedger = await _ledgerRepository.GetByIdempotencyKeyAsync(ledgerKey);

            var now = DateTime.UtcNow;

            if (request.Decision == RefundDecision.Approve)
            {
                var approvedAmount = request.ApprovedAmountBvc!.Value;

                if (existingLedger == null)
                {
                    // Lock wallet + cộng BVC.
                    var wallet = await _walletRepository.GetByUserIdForUpdateAsync(refundRequest.UserId);
                    if (wallet == null)
                    {
                        throw new NotFoundException(
                            ApiErrorMessages.Wallet.NotFound(refundRequest.UserId));
                    }

                    wallet.AvailableBalance += approvedAmount;
                    wallet.UpdatedAt = now;

                    var ledgerEntry = new BvcLedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        UserId = refundRequest.UserId,
                        Type = LedgerEntryType.AdminCredit,
                        Amount = approvedAmount,
                        RelatedLobbyId = null,
                        RelatedBookingId = null,
                        RelatedPaymentRef = $"refund-request:{requestId}",
                        IdempotencyKey = ledgerKey,
                        BalanceSnapshot = wallet.AvailableBalance,
                        Note = $"[Refund] Request={requestId}; Admin={adminUserId}; {request.AdminNote}",
                        CreatedAt = now
                    };

                    await _walletRepository.UpdateAsync(wallet);
                    await _ledgerRepository.AddAsync(ledgerEntry);

                    refundRequest.ResultLedgerEntryId = ledgerEntry.Id;
                }
                else
                {
                    // Replay: ledger đã có → reuse.
                    refundRequest.ResultLedgerEntryId = existingLedger.Id;
                }
            }

            // Cập nhật refund request status + admin info.
            refundRequest.Status = request.Decision == RefundDecision.Approve
                ? RefundRequestStatus.Approved
                : RefundRequestStatus.Rejected;
            refundRequest.ApprovedAmountBvc = request.Decision == RefundDecision.Approve
                ? request.ApprovedAmountBvc!.Value
                : null;
            refundRequest.AdminNote = request.AdminNote.Trim();
            refundRequest.ResolvedByAdminId = adminUserId;
            refundRequest.ResolvedAt = now;
            refundRequest.UpdatedAt = now;

            await _refundRequestRepository.UpdateAsync(refundRequest);

            // BR-RISK-05: ghi PlayerActionHistory.
            var actionType = request.Decision == RefundDecision.Approve
                ? AdminActionType.AdminCredit
                : AdminActionType.AccountStatusChange;

            var metadata = new Dictionary<string, object?>
            {
                ["refundRequestId"] = requestId,
                ["ledgerEntryId"] = refundRequest.ResultLedgerEntryId,
                ["requestedAmountBvc"] = refundRequest.RequestedAmountBvc,
                ["approvedAmountBvc"] = refundRequest.ApprovedAmountBvc,
                ["decision"] = request.Decision.ToString()
            };

            var historyEntry = new PlayerActionHistory
            {
                Id = Guid.NewGuid(),
                UserId = refundRequest.UserId,
                ActionType = actionType,
                ActionBy = adminUserId,
                Reason = request.AdminNote.Trim(),
                Metadata = JsonSerializer.Serialize(metadata),
                CreatedAt = now
            };

            _db.PlayerActionHistories.Add(historyEntry);

            await _refundRequestRepository.SaveChangesAsync(cancellationToken);

            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(cancellationToken);
            }

            _logger.LogWarning(
                "Refund request resolved. RequestId={RequestId}, Decision={Decision}, AdminId={AdminId}, UserId={UserId}, RequestedBvc={Requested}, ApprovedBvc={Approved}",
                requestId, request.Decision, adminUserId, refundRequest.UserId,
                refundRequest.RequestedAmountBvc, refundRequest.ApprovedAmountBvc);

            return await BuildResolvedDtoAsync(refundRequest, cancellationToken);
        }
        catch
        {
            if (ownedTx != null)
            {
                await ownedTx.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    // ===== Helpers =====

    /// <summary>
    /// GAP-R6-WAL-02: detect Postgres serialization failure (40001) hoặc deadlock (40P01)
    /// để retry toàn bộ transaction trong <see cref="ResolveAsync"/>.
    /// </summary>
    private static bool IsSerializationFailure(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("40001", StringComparison.Ordinal)
            || msg.Contains("40P01", StringComparison.Ordinal)
            || msg.Contains("could not serialize", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("deadlock", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<RefundRequestResponseDto> BuildResolvedDtoAsync(
        BvcRefundRequest request,
        CancellationToken cancellationToken)
    {
        var dto = MapToDto(request, includeEmail: true);
        var ledger = await _ledgerRepository.GetByIdAsync(request.RelatedLedgerEntryId);
        if (ledger != null)
        {
            dto.RelatedLedgerEntryType = ledger.Type;
            dto.RelatedLedgerEntryAmount = ledger.Amount;
        }
        return dto;
    }

    private static RefundRequestResponseDto MapToDto(BvcRefundRequest r, bool includeEmail = false)
    {
        return new RefundRequestResponseDto
        {
            Id = r.Id,
            UserId = r.UserId,
            UserEmail = includeEmail ? r.User?.Email : null,
            RelatedLedgerEntryId = r.RelatedLedgerEntryId,
            RequestedAmountBvc = r.RequestedAmountBvc,
            ApprovedAmountBvc = r.ApprovedAmountBvc,
            PlayerReason = r.PlayerReason,
            AdminNote = r.AdminNote,
            Status = r.Status,
            ResolvedByAdminId = r.ResolvedByAdminId,
            ResolvedAt = r.ResolvedAt,
            ResultLedgerEntryId = r.ResultLedgerEntryId,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}