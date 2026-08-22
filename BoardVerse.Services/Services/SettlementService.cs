using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
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
using System.Transactions;

namespace BoardVerse.Services.Services;

/// <summary>
/// Settlement = giải ngân tiền cọc từ BoardVerse master về tài khoản cafe manager.
/// BR-09 + BR-18: Tiền cọc được cấn trừ 1 lần khi phiên PAID → release to cafe manager.
/// Retry: khi SePay fail → set CafeSettlement.Status = Failed, giữ BookingDeposit.Status = Paid
/// để <see cref="BoardVerse.API.BackgroundServices.SettlementRetryJob"/> có thể retry.
/// W-04: Payout tính từ BvcLedgerEntry Type=DepositCapture thay vì BookingDeposit.Amount.
/// </summary>
public class SettlementService : ISettlementService
{
    private readonly IBookingDepositRepository _depositRepository;
    private readonly ICafeSettlementRepository _settlementRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly IBvcLedgerEntryRepository _ledgerRepository;
    private readonly ISePayClient _sePayClient;
    private readonly ISePayAccountService _sePayAccountService;
    private readonly ILogger<SettlementService> _logger;
    private readonly BoardVerseDbContext _db;

    public SettlementService(
        IBookingDepositRepository depositRepository,
        ICafeSettlementRepository settlementRepository,
        ICafeRepository cafeRepository,
        IActiveSessionRepository activeSessionRepository,
        IBvcLedgerEntryRepository ledgerRepository,
        ISePayClient sePayClient,
        ISePayAccountService sePayAccountService,
        ILogger<SettlementService> logger,
        BoardVerseDbContext db)
    {
        _depositRepository = depositRepository;
        _settlementRepository = settlementRepository;
        _cafeRepository = cafeRepository;
        _activeSessionRepository = activeSessionRepository;
        _ledgerRepository = ledgerRepository;
        _sePayClient = sePayClient;
        _sePayAccountService = sePayAccountService;
        _logger = logger;
        _db = db;
    }

    /// <summary>
    /// Release deposit của session vào tài khoản cafe.
    /// </summary>
    public async Task<CafeSettlement> ReleaseSessionDepositAsync(
        Guid cafeId,
        Guid sessionId,
        Guid activeSessionId, CancellationToken cancellationToken = default)
    {
        var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var session = await _activeSessionRepository.GetByIdAsync(activeSessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFound(cafeId, activeSessionId));

        if (session.Status != GroupSessionStatus.Paid)
        {
            throw new ConflictException(ApiErrorMessages.Pos.SessionMustBePaidForDepositSettlement);
        }

        // Verify master SePayAccount exists (for audit purposes - actual transfer uses cafe's bank)
        var masterAccount = await _sePayAccountService.GetRawMasterAccountAsync();
        if (masterAccount == null)
        {
            throw new ConflictException(ApiErrorMessages.Pos.MasterAccountNotConfigured);
        }

        // Gap 4 (fix): Destination = cafe manager's SePay bank account, KHÔNG phải master account.
        if (string.IsNullOrWhiteSpace(cafe.SePayAccountNumber) || string.IsNullOrWhiteSpace(cafe.SePayBankCode))
        {
            throw new ConflictException(
                ApiErrorMessages.Pos.SePayBankNotConfigured(cafe.Name ?? ""));
        }

        var deposit = await _depositRepository.GetByActiveSessionIdAsync(activeSessionId)
            ?? throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);

        if (deposit.Status != BookingDepositStatus.Paid)
        {
            throw new ConflictException(ApiErrorMessages.Pos.DepositNotPaid);
        }

        // W-04: Query DepositCapture from BVC ledger instead of using BookingDeposit.Amount directly.
        // This ensures we use the actual captured BVC amount from the ledger.
        var depositCaptureEntries = await _db.BvcLedgerEntries
            .Where(e => e.RelatedBookingId == deposit.BookingId
                && e.Type == LedgerEntryType.DepositCapture)
            .ToListAsync();

        long netTransfer;
        if (depositCaptureEntries.Count > 0)
        {
            // Sum all DepositCapture entries for this booking
            long sum = 0;
            foreach (var entry in depositCaptureEntries)
            {
                checked { sum += entry.Amount; }
            }
            netTransfer = sum;
        }
        else
        {
            // Fallback to deposit.Amount if no ledger entries found (backward compat)
            netTransfer = (long)deposit.Amount;
        }

        var settlement = new CafeSettlement
        {
            CafeId = cafeId,
            CafeManagerId = cafe.ManagerId,
            ActiveSessionId = activeSessionId,
            BookingDepositId = deposit.Id,
            DepositAmount = deposit.Amount,
            FeeAmount = 0,
            NetTransferAmount = netTransfer,
            Status = CafeSettlementStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // P0 Fix #1: Wrap in transaction to ensure atomicity
        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        await _settlementRepository.AddAsync(settlement);
        await _settlementRepository.SaveChangesAsync();

        try
        {
            var transferRequest = new CreateTransferRequest(
                ToBankAccount: cafe.SePayBankCode,
                ToAccountNumber: cafe.SePayAccountNumber,
                Amount: netTransfer,
                Description: $"BoardVerse settlement - session {activeSessionId}",
                ReferenceId: $"settlement_{settlement.Id:N}");

            var transferResponse = await _sePayClient.CreateTransferAsync(transferRequest);

            settlement.Status = CafeSettlementStatus.Succeeded;
            settlement.SePayTransferId = transferResponse.TransferId ?? settlement.SePayTransferId;
            settlement.TransferredAt = DateTime.UtcNow;

            // Chỉ set deposit = Released khi transfer succeed.
            deposit.Status = BookingDepositStatus.Released;
            deposit.ReleasedAt = DateTime.UtcNow;
            deposit.SePayTransferId = transferResponse.TransferId;

            // Mark transaction as complete (will commit when disposed)
            transaction.Complete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SePay transfer failed for settlement {SettlementId}. Will retry later.", settlement.Id);
            settlement.Status = CafeSettlementStatus.Failed;
            settlement.FailureReason = ex.Message;
            // Deposit vẫn ở Paid — sẽ được retry bởi SettlementRetryJob.
            throw;
        }
        finally
        {
            settlement.UpdatedAt = DateTime.UtcNow;
            await _settlementRepository.UpdateAsync(settlement);
            await _settlementRepository.SaveChangesAsync();

            deposit.UpdatedAt = DateTime.UtcNow;
            await _depositRepository.UpdateAsync(deposit);
            await _depositRepository.SaveChangesAsync();

            _logger.LogInformation(
                "Settlement {SettlementId} for cafe {CafeId} session {SessionId}: Status={Status}, Amount={Amount}",
                settlement.Id, cafeId, activeSessionId, settlement.Status, netTransfer);
        }

        return settlement;
    }

    public async Task<IReadOnlyList<CafeSettlement>> GetPendingSettlementsAsync(Guid cafeId, Guid actorUserId, string actorRole)
    {
        // C8: Verify cafe operator access. Admin bypasses. Manager: cafe.ManagerId.
        // CafeStaff: must be linked to the cafe.
        var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        if (actorRole == "Admin")
        {
            // bypass
        }
        else if (actorRole == "Manager")
        {
            if (cafe.ManagerId != actorUserId)
            {
                throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
            }
        }
        else if (actorRole == "CafeStaff")
        {
            if (!await _cafeRepository.IsStaffMemberExistsAsync(cafeId, actorUserId))
            {
                throw new ForbiddenException(ApiErrorMessages.Cafe.InventoryManagerForbidden(cafeId));
            }
        }
        else
        {
            throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
        }

        return await _settlementRepository.GetPendingAsync(cafeId);
    }

    /// <summary>
    /// W-06: Admin list settlements với filter + phân trang.
    /// </summary>
    public Task<PaginatedResponse<SettlementListItemDto>> GetPagedAsync(SettlementListQuery query, CancellationToken cancellationToken = default) =>
        _settlementRepository.GetPagedAsync(query);

    /// <summary>
    /// W-06: Admin manually override a failed settlement after retry exhaustion.
    /// Sets Status = Overridden, OverrideBy = adminId, OverrideAt = now.
    /// </summary>
    public async Task<CafeSettlement> OverrideSettlementAsync(Guid settlementId, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var settlement = await _settlementRepository.GetByIdAsync(settlementId)
            ?? throw new NotFoundException(ApiErrorMessages.Settlement.NotFound(settlementId));

        if (settlement.Status == CafeSettlementStatus.Overridden)
        {
            throw new ConflictException(ApiErrorMessages.Settlement.AlreadyOverridden);
        }

        settlement.Status = CafeSettlementStatus.Overridden;
        settlement.OverrideBy = adminUserId;
        settlement.OverrideAt = DateTime.UtcNow;
        settlement.UpdatedAt = DateTime.UtcNow;

        await _settlementRepository.UpdateAsync(settlement);
        await _settlementRepository.SaveChangesAsync();

        _logger.LogWarning(
            "Settlement {SettlementId} manually overridden by admin {AdminId}.",
            settlementId, adminUserId);

        return settlement;
    }
}
