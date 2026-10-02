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

/// <inheritdoc cref="ICoolingOffService"/>
public class CoolingOffService : ICoolingOffService
{
    private readonly IWalletRepository _walletRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly IBvcLedgerEntryRepository _ledgerRepository;
    private readonly BoardVerseDbContext _db;
    private readonly ILogger<CoolingOffService> _logger;

    // BR-NEW-10 §XI.1 thresholds
    private const int FailureCountThreshold = 3;
    private const int FailureWindowDays = 7;
    private const long ForfeitAmountThreshold = 500L; // 500 BVC = 500.000 VND (BR-NEW-10 §XI.1, corrected 2026-08-12 from 500_000L)
    private const int ForfeitWindowDays = 30;
    private static readonly TimeSpan CoolingOffDuration = TimeSpan.FromDays(30);
    private const int MaxExtendDays = 90;

    // 2026-10-02: Cooling-off KHÔNG còn nhân RiskMultiplier lên cọc (đơn giản hóa theo yêu cầu).
    // BR-NEW-10 §XI.2 "Cọc ×2 khi cooling-off" và "Cọc ×3 khi escalate" đã bỏ.
    // Wallet.RiskMultiplier chỉ được quản lý bởi risk score recompute job (BR-RISK-03) — không bị cooling-off trigger.
    // Hệ quả: cọc = baseDeposit (20% × cafeBasePrice/1000 × maxPlayers) bất kể cooling-off active hay không.
    //
    // Trước đây có 2 helpers:
    //   - ComputeRiskMultiplierFromRiskScore(int riskScore, bool isCoolingOff): 1.0 + score/100 (×2 nếu cooling-off)
    //   - ComputeEscalationRiskMultiplier(int riskScore): (1.0 + score/100) × 3.0
    // → ĐÃ XÓA 2026-10-02 vì cooling-off không nhân multiplier nữa.

    public CoolingOffService(
        IWalletRepository walletRepository,
        ILobbyRepository lobbyRepository,
        IBvcLedgerEntryRepository ledgerRepository,
        BoardVerseDbContext db,
        ILogger<CoolingOffService> logger)
    {
        _walletRepository = walletRepository;
        _lobbyRepository = lobbyRepository;
        _ledgerRepository = ledgerRepository;
        _db = db;
        _logger = logger;
    }

    public async Task<int> DetectAndActivateAsync(DateTime now, int batchSize, CancellationToken ct = default)
    {
        // Quét wallet active (không bao gồm suspended/banned) theo batch.
        var candidates = await _walletRepository.GetActiveWalletsPagedAsync(batchSize);
        var activated = 0;

        foreach (var wallet in candidates)
        {
            if (ct.IsCancellationRequested) break;

            // Skip nếu đã trong cooling-off (ExpireOverdueAsync sẽ xử lý).
            if (wallet.IsCoolingOff) continue;

            var signals = await DetectSignalsAsync(wallet.UserId, now, ct);
            var triggerReason = EvaluateTrigger(signals);

            if (triggerReason == null) continue;

            await ActivateCoolingOffAsync(wallet, now, triggerReason, ct);
            activated++;
        }

        if (activated > 0)
        {
            _logger.LogInformation(
                "[BR-NEW-10] DetectAndActivateAsync activated {Count} cooling-off(s) at {NowUtc:O}",
                activated, now);
        }

        return activated;
    }

    public async Task<int> ExpireOverdueAsync(DateTime now, int batchSize, CancellationToken ct = default)
    {
        var overdue = await _walletRepository.GetActiveCoolingOffWalletsPagedAsync(batchSize);
        var deactivated = 0;

        foreach (var wallet in overdue)
        {
            if (ct.IsCancellationRequested) break;

            if (!wallet.IsCoolingOff || wallet.CoolingOffExpiresAt == null) continue;

            wallet.IsCoolingOff = false;
            wallet.CoolingOffExpiresAt = null;
            // BR-RISK-03: gia hạn cooling-off không thay đổi RiskMultiplier sau khi expire.
            // RiskMultiplier sẽ được risk score recompute job tính lại dựa trên signals hiện tại.
            wallet.UpdatedAt = now;

            await _walletRepository.UpdateAsync(wallet);
            deactivated++;
        }

        if (deactivated > 0)
        {
            await _walletRepository.SaveChangesAsync();
            _logger.LogInformation(
                "[BR-NEW-10] ExpireOverdueAsync deactivated {Count} cooling-off(s) at {NowUtc:O}",
                deactivated, now);
        }

        return deactivated;
    }

    public async Task EscalateAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var wallet = await _walletRepository.GetByUserIdForUpdateAsync(userId);
        if (wallet == null)
        {
            _logger.LogWarning("[BR-NEW-10] EscalateAsync: wallet not found for userId={UserId}", userId);
            return;
        }

        if (!wallet.IsCoolingOff)
        {
            _logger.LogWarning(
                "[BR-NEW-10] EscalateAsync called for user not in cooling-off: userId={UserId}", userId);
            return;
        }

        var now = DateTime.UtcNow;

        // BR-NEW-10 §XI.2: gia hạn cooling-off 30 ngày.
        // 2026-10-02: KHÔNG nhân RiskMultiplier lên ×3 nữa (đơn giản hóa).
        // RiskMultiplier được quản lý riêng bởi risk score recompute job (BR-RISK-03), không bị cooling-off trigger.
        wallet.CoolingOffExpiresAt = now.Add(CoolingOffDuration);
        wallet.UpdatedAt = now;

        await _walletRepository.UpdateAsync(wallet);
        await _walletRepository.SaveChangesAsync();

        _logger.LogInformation(
            "[BR-NEW-10] EscalateAsync: userId={UserId} cooling-off extended to {ExpiresAt} reason={Reason}",
            userId, wallet.CoolingOffExpiresAt, reason);
    }

    public async Task<(int TimeoutFailedCount7d, int HostCancelledCount7d, long ForfeitAmount30d)> DetectSignalsAsync(
        Guid userId,
        DateTime now,
        CancellationToken ct = default)
    {
        var since7d = now.AddDays(-FailureWindowDays);
        var since30d = now.AddDays(-ForfeitWindowDays);

        var timeoutCount = await _lobbyRepository.CountFailuresByTypeForHostAsync(
            userId, since7d, now, LobbyStatus.TimeoutFailed);
        var hostCancelledCount = await _lobbyRepository.CountFailuresByTypeForHostAsync(
            userId, since7d, now, LobbyStatus.HostCancelled);
        var dissolvedCount = await _lobbyRepository.CountFailuresByTypeForHostAsync(
            userId, since7d, now, LobbyStatus.Dissolved);
        var cancelCount = hostCancelledCount + dissolvedCount;

        // Forfeit amount từ ledger (LedgerEntryType.DepositForfeit) trong 30 ngày.
        var forfeitAmountDecimal = await _ledgerRepository.SumForfeitAsync(userId, since30d);
        var forfeitAmount = (long)forfeitAmountDecimal;

        return (timeoutCount, cancelCount, forfeitAmount);
    }

    private static string? EvaluateTrigger(
        (int TimeoutFailedCount7d, int HostCancelledCount7d, long ForfeitAmount30d) signals)
    {
        // BR-NEW-10 §XI.1:
        // • 3 lobby timeoutFailed liên tiếp trong 7 ngày, HOẶC
        // • 3 lobby hostCancelled (sau grace) liên tiếp trong 7 ngày, HOẶC
        // • Tổng cọc forfeit/no-show > 500k BVC trong 30 ngày.
        if (signals.TimeoutFailedCount7d >= FailureCountThreshold)
        {
            return $"{FailureCountThreshold}+ TimeoutFailed trong {FailureWindowDays} ngày";
        }
        if (signals.HostCancelledCount7d >= FailureCountThreshold)
        {
            return $"{FailureCountThreshold}+ HostCancelled trong {FailureWindowDays} ngày";
        }
        if (signals.ForfeitAmount30d > ForfeitAmountThreshold)
        {
            return $"Tổng forfeit > {ForfeitAmountThreshold:N0} BVC trong {ForfeitWindowDays} ngày";
        }
        return null;
    }

    private async Task ActivateCoolingOffAsync(Wallet wallet, DateTime now, string reason, CancellationToken ct)
    {
        // BR-NEW-10 §XI.2: cooling-off 30 ngày.
        // 2026-10-02: KHÔNG nhân RiskMultiplier lên ×2 nữa (đơn giản hóa theo yêu cầu).
        // BR-NEW-10 §XI.2 "cọc ×2" đã bỏ — cọc chỉ phụ thuộc (cafeBasePrice × 20% × maxPlayers).
        // Wallet.RiskMultiplier giữ nguyên (do risk score recompute job quản lý, BR-RISK-03).
        wallet.IsCoolingOff = true;
        wallet.CoolingOffExpiresAt = now.Add(CoolingOffDuration);
        wallet.UpdatedAt = now;

        await _walletRepository.UpdateAsync(wallet);
        await _walletRepository.SaveChangesAsync();

        _logger.LogInformation(
            "[BR-NEW-10] Cooling-off ACTIVATED: userId={UserId} expiresAt={ExpiresAt} reason={Reason}",
            wallet.UserId, wallet.CoolingOffExpiresAt, reason);
    }

    /// <summary>
    /// Helper cũ (gap 4.1) — load riskScore từ PlayerRiskScore table.
    /// 2026-10-02: ĐÃ XÓA việc sử dụng vì cooling-off không còn nhân RiskMultiplier nữa.
    /// Giữ method nếu sau này cần dùng lại cho audit/reporting.
    /// </summary>
    private async Task<int> GetCurrentRiskScoreAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var score = await _db.PlayerRiskScores
                .AsNoTracking()
                .Where(r => r.UserId == userId)
                .Select(r => (int?)r.RiskScore)
                .FirstOrDefaultAsync(ct);
            return score ?? 75;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Gap 4.1] Failed to load riskScore for userId={UserId}, using fallback 75", userId);
            return 75;
        }
    }

    public async Task ExtendAsync(Guid adminUserId, Guid targetUserId, int additionalDays, string reason, CancellationToken ct = default)
    {
        if (additionalDays < 1 || additionalDays > MaxExtendDays)
        {
            throw new BadRequestException(
                ApiErrorMessages.AdminModeration.ExtendAdditionalDaysRange(MaxExtendDays));
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
        {
            throw new BadRequestException(ApiErrorMessages.AdminModeration.ExtendReasonMinLength);
        }

        var wallet = await _walletRepository.GetByUserIdForUpdateAsync(targetUserId);
        if (wallet == null)
        {
            throw new NotFoundException(ApiErrorMessages.AdminModeration.WalletNotFound(targetUserId));
        }

        if (!wallet.IsCoolingOff)
        {
            throw new ConflictException(ApiErrorMessages.AdminModeration.UserNotInCoolingOff);
        }

        var now = DateTime.UtcNow;
        var previousExpiresAt = wallet.CoolingOffExpiresAt;
        var baseExpiry = (previousExpiresAt.HasValue && previousExpiresAt.Value > now)
            ? previousExpiresAt.Value
            : now;
        var newExpiresAt = baseExpiry.AddDays(additionalDays);
        wallet.CoolingOffExpiresAt = newExpiresAt;
        wallet.UpdatedAt = now;

        await _walletRepository.UpdateAsync(wallet);

        // Ghi audit log với AdminActionType.PlayedTimeDisputed = 40 không phù hợp;
        // dùng AccountStatusChange (BR-RISK-05/06 general-purpose audit).
        var historyEntry = new PlayerActionHistory
        {
            Id = Guid.NewGuid(),
            UserId = targetUserId,
            ActionType = AdminActionType.AccountStatusChange,
            ActionBy = adminUserId,
            Reason = $"Admin extend cooling-off thêm {additionalDays} ngày: {reason.Trim()}",
            Metadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                adminUserId,
                targetUserId,
                previousExpiresAt,
                newExpiresAt,
                additionalDays,
                reason = reason.Trim(),
                action = "ExtendCoolingOff"
            }),
            CreatedAt = now
        };

        _db.PlayerActionHistories.Add(historyEntry);
        await _walletRepository.SaveChangesAsync();

        _logger.LogInformation(
            "[BR-NEW-10] Admin {AdminId} extended cooling-off for user {UserId}: +{Days}d (expiresAt {Previous} → {New})",
            adminUserId, targetUserId, additionalDays, previousExpiresAt, newExpiresAt);
    }
}
