using BoardVerse.Core.Common;
using BoardVerse.Core.Constants;
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

        // GAP #4 FIX: Lookup deposit via ActiveSessionId, fallback to Lobby chain
        // This handles both legacy flows (deposit.ActiveSessionId set directly)
        // and new Reservation flow (need to traverse Lobby → BookingDeposit)
        BookingDeposit? deposit = null;

        // Strategy 1: Direct ActiveSessionId lookup
        if (activeSessionId != Guid.Empty)
        {
            deposit = await _depositRepository.GetByActiveSessionIdAsync(activeSessionId);
        }

        // Strategy 2: Fallback - traverse ActiveSession → Lobby → BookingDeposit
        // session entity đã include Lobby (line 69-70 calls GetByIdAsync which includes Lobby)
        if (deposit == null && session?.LobbyId != null)
        {
            var lobby = session.Lobby;
            if (lobby?.BookingId != null)
            {
                deposit = await _depositRepository.GetByIdAsync(lobby.BookingId.Value);
            }
        }

        // Strategy 3: Fallback - direct BookingDeposit lookup by sessionId (Reservation.Id)
        if (deposit == null && sessionId != Guid.Empty)
        {
            deposit = await _depositRepository.GetByIdAsync(sessionId);
        }

        if (deposit == null)
        {
            throw new NotFoundException(ApiErrorMessages.Pos.DepositMissingForSettlement);
        }

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
        _settlementRepository.GetPagedAsync(query, cancellationToken);

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

    /// <summary>
    /// W-07: Tổng hợp giải ngân theo ngày. Mặc định = hôm nay theo giờ VN (UTC+7).
    /// </summary>
    public async Task<SettlementDailySummaryDto> GetDailySummaryAsync(
        DateOnly? date = null,
        CancellationToken cancellationToken = default)
    {
        var vnTz = CafeSchedule.VietnamTz;
        var targetDate = date ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTz));

        // Build UTC range [date 00:00 VN, date+1 00:00 VN).
        var startVnLocal = targetDate.ToDateTime(new TimeOnly(0, 0));
        var endVnLocal = targetDate.AddDays(1).ToDateTime(new TimeOnly(0, 0));
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startVnLocal, vnTz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endVnLocal, vnTz);

        // Lấy tất cả settlement trong range (repository đã filter theo status + TransferredAt/CreatedAt).
        var settlements = await _settlementRepository.GetForDailySummaryAsync(
            startUtc, endUtc, cancellationToken);

        // Lấy thông tin cafe (tên + SePay config) cho các cafeId xuất hiện.
        var cafeIds = settlements.Select(s => s.CafeId).Distinct().ToList();
        var cafeLookup = await _db.Cafes.AsNoTracking()
            .Where(c => cafeIds.Contains(c.Id))
            .ToDictionaryAsync(
                c => c.Id,
                c => new
                {
                    c.Name,
                    c.ManagerId,
                    c.SePayBankCode,
                    c.SePayAccountNumber
                },
                cancellationToken);

        // Group theo cafeId.
        var grouped = settlements
            .GroupBy(s => s.CafeId)
            .Select(g =>
            {
                cafeLookup.TryGetValue(g.Key, out var cafe);

                var byStatus = Enum.GetValues<CafeSettlementStatus>()
                    .Select(status =>
                    {
                        var statusItems = g.Where(x => x.Status == status).ToList();
                        return new SettlementStatusBreakdownDto
                        {
                            Status = status,
                            TotalAmount = statusItems.Sum(x => x.DepositAmount),
                            TotalDepositAmount = statusItems.Sum(x => x.DepositAmount),
                            TotalNetTransferAmount = statusItems.Sum(x => x.NetTransferAmount),
                            Count = statusItems.Count,
                            // Ưu tiên TransferredAt cho Succeeded/Overridden, fallback CreatedAt.
                            LatestAt = statusItems
                                .Select(x => x.TransferredAt ?? x.CreatedAt)
                                .DefaultIfEmpty()
                                .Max()
                        };
                    })
                    .ToList();

                var totalDeposit = g.Sum(x => x.DepositAmount);
                var totalTransferred = g.Where(x => x.Status == CafeSettlementStatus.Succeeded).Sum(x => x.NetTransferAmount);
                var totalPending = g.Where(x => x.Status == CafeSettlementStatus.Pending
                                                || x.Status == CafeSettlementStatus.Retrying).Sum(x => x.NetTransferAmount);
                var totalFailed = g.Where(x => x.Status == CafeSettlementStatus.Failed).Sum(x => x.NetTransferAmount);
                var totalOverridden = g.Where(x => x.Status == CafeSettlementStatus.Overridden).Sum(x => x.NetTransferAmount);

                return new CafeDailySettlementDto
                {
                    CafeId = g.Key,
                    CafeName = cafe?.Name,
                    CafeManagerId = cafe?.ManagerId ?? Guid.Empty,
                    SePayBankCode = cafe?.SePayBankCode,
                    SePayAccountNumber = cafe?.SePayAccountNumber,
                    TotalDepositAmount = totalDeposit,
                    TotalToTransfer = totalTransferred + totalPending + totalOverridden,
                    TotalTransferred = totalTransferred,
                    TotalPending = totalPending,
                    TotalFailed = totalFailed,
                    TotalOverridden = totalOverridden,
                    TotalCount = g.Count(),
                    LatestActivityAt = g.Max(x => x.TransferredAt ?? x.CreatedAt),
                    ByStatus = byStatus,
                    SettlementIds = g.Select(x => x.Id).ToList()
                };
            })
            // Quán có số tiền cần chuyển lớn nhất lên đầu.
            .OrderByDescending(c => c.TotalToTransfer)
            .ThenBy(c => c.CafeName)
            .ToList();

        return new SettlementDailySummaryDto
        {
            Date = targetDate.ToString("yyyy-MM-dd"),
            Timezone = vnTz.Id,
            QueryStartUtc = startUtc,
            QueryEndUtc = endUtc,
            CafeCount = grouped.Count,
            TotalSettlementCount = settlements.Count,
            GrandTotalDeposit = grouped.Sum(c => c.TotalDepositAmount),
            GrandTotalToTransfer = grouped.Sum(c => c.TotalToTransfer),
            GrandTotalTransferred = grouped.Sum(c => c.TotalTransferred),
            GrandTotalFailed = grouped.Sum(c => c.TotalFailed),
            Cafes = grouped
        };
    }
}
