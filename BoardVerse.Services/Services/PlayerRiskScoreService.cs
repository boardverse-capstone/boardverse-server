using System.Text.Json;
using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <inheritdoc cref="IPlayerRiskScoreService"/>
public class PlayerRiskScoreService : IPlayerRiskScoreService
{
    // BR-RISK-01 — signal weights
    private const int W_TimeoutFailed7d = 15;
    private const int W_HostCancelled7d = 15;
    private const int W_Forfeit30d = 20;       // × / 1000
    private const int W_SpamSamePlayDate30d = 10;
    private const int W_CreateCancelUnder5min = 25;

    private readonly IPlayerRiskScoreRepository _riskRepo;
    private readonly ILobbyRepository _lobbyRepo;
    private readonly IBvcLedgerEntryRepository _ledgerRepo;
    private readonly BoardVerseDbContext _db;
    private readonly IPlayerAlertService _alertService;
    private readonly ILogger<PlayerRiskScoreService> _logger;

    public PlayerRiskScoreService(
        IPlayerRiskScoreRepository riskRepo,
        ILobbyRepository lobbyRepo,
        IBvcLedgerEntryRepository ledgerRepo,
        BoardVerseDbContext db,
        IPlayerAlertService alertService,
        ILogger<PlayerRiskScoreService> logger)
    {
        _riskRepo = riskRepo;
        _lobbyRepo = lobbyRepo;
        _ledgerRepo = ledgerRepo;
        _db = db;
        _alertService = alertService;
        _logger = logger;
    }

    public int ComputeRiskScore(IReadOnlyDictionary<string, int> signals)
    {
        // BR-RISK-01 — clamp(0, 100).
        var total = signals.GetValueOrDefault("SIG-01", 0) * W_TimeoutFailed7d
            + signals.GetValueOrDefault("SIG-02", 0) * W_HostCancelled7d
            + signals.GetValueOrDefault("SIG-03", 0) * W_Forfeit30d / 1000
            + signals.GetValueOrDefault("SIG-04", 0) * W_SpamSamePlayDate30d
            + signals.GetValueOrDefault("SIG-05", 0) * 5
            + signals.GetValueOrDefault("SIG-06", 0) * 8
            + signals.GetValueOrDefault("SIG-08", 0) * W_CreateCancelUnder5min
            + signals.GetValueOrDefault("SIG-10", 0) * 20;
        return Math.Clamp(total, 0, 100);
    }

    public RiskLevel ResolveRiskLevel(int riskScore) => riskScore switch
    {
        < 30 => RiskLevel.Low,
        < 50 => RiskLevel.Medium,
        < 75 => RiskLevel.High,
        _ => RiskLevel.Critical
    };

    public async Task<PlayerRiskScore?> RecomputeForUserAsync(Guid userId, DateTime now, CancellationToken ct = default)
    {
        var signals = await CollectSignalsAsync(userId, now, ct);

        var score = ComputeRiskScore(signals);
        var level = ResolveRiskLevel(score);

        var snapshot = await _riskRepo.GetByUserIdAsync(userId) ?? new PlayerRiskScore
        {
            UserId = userId,
            CreatedAt = now
        };

        // BR-RISK-04 + Gap 4.7 (2026-10-02): auto-update Wallet.AccountStatus khi riskScore cross thresholds.
        // Mapping BR-RISK-04 §XVI.5:
        //   0..29  → low       → Active
        //   30..49 → medium    → Warning
        //   50..74 → high      → Restricted
        //   75..100 → critical → Restricted (Suspension chỉ do admin manual review)
        // Rule bổ sung: KHÔNG downgrade AccountStatus nếu admin đã set cao hơn (eg: admin đã ban).
        // Backward-compat: nếu Wallet.AccountStatus do admin set = Banned → KHÔNG touch.
        var previousLevel = snapshot.RiskLevel;
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet != null)
        {
            var computedAccountStatus = MapRiskScoreToAccountStatus(score);
            if (wallet.AccountStatus != AccountStatus.Banned
                && wallet.AccountStatus != AccountStatus.Suspended)
            {
                // Cho phép upgrade (active→warning→restricted) nhưng KHÔNG downgrade
                // (restricted→active chỉ qua admin manual review).
                if (IsAccountStatusUpgrade(wallet.AccountStatus, computedAccountStatus))
                {
                    _logger.LogInformation(
                        "[Gap 4.7] Auto-update AccountStatus. UserId={UserId}, From={From}, To={To}, RiskScore={Score}",
                        userId, wallet.AccountStatus, computedAccountStatus, score);
                    wallet.AccountStatus = computedAccountStatus;
                }
            }
        }

        snapshot.RiskScore = score;
        snapshot.RiskLevel = level;
        snapshot.Signals = JsonSerializer.Serialize(signals);
        snapshot.LastUpdated = now;

        await _riskRepo.UpsertAsync(snapshot);

        // BR-RISK-11 — append history.
        var history = new RiskScoreHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RiskScore = score,
            RiskLevel = level,
            Signals = snapshot.Signals,
            SnapshotDate = DateOnly.FromDateTime(now),
            CreatedAt = now
        };
        await _riskRepo.AppendHistoryAsync(history);

        await _db.SaveChangesAsync(ct);

        // BR-RISK-02 — alert nếu level tăng critical.
        if (level == RiskLevel.Critical && previousLevel != RiskLevel.Critical)
        {
            try
            {
                await _alertService.EnsureAlertForSignalsAsync(
                    userId, score, level, previousLevel, snapshot.Signals);
                _logger.LogInformation(
                    "PlayerRiskScoreService.ComputedCritical user={UserId} score={Score}",
                    userId, score);
            }
            catch (Exception ex)
            {
                // Gap 4.4 (2026-10-02): nuốt exception để flow chính không bị block,
                // NHƯNG log với structured tag "alert_failure_counter" để metric pipeline (Seq/Datadog)
                // count + page on-call nếu > threshold (vd: 5 lần/giờ = vấn đề).
                // Trước đây: chỉ LogError thường, không có counter → admin không phát hiện
                // khi alert service down hàng loạt.
                Interlocked.Increment(ref _alertFailureCounter);
                var totalFailures = Interlocked.CompareExchange(ref _alertFailureCounter, 0, 0);
                _logger.LogError(ex,
                    "[Gap 4.4] PlayerAlertService.EnsureAlert failed for user {UserId}. TotalFailures={TotalFailures}",
                    userId, totalFailures);

                // Nếu counter vượt threshold → log riêng để dễ alert (tag "alert_service_degraded").
                if (totalFailures > 0 && totalFailures % 5 == 0)
                {
                    _logger.LogCritical(
                        "[Gap 4.4] PlayerAlertService đã fail {TotalFailures} lần — có thể alert service degraded. Check DB connectivity + IPlayerAlertRepository.",
                        totalFailures);
                }
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Gap 4.4 (2026-10-02): Counter metric cho PlayerAlertService failures.
    /// Reset không cần — total cumulative cho monitoring dashboard.
    /// Đọc qua <see cref="GetAlertFailureCount"/> từ background job / admin endpoint.
    /// </summary>
    private long _alertFailureCounter;

    /// <summary>Gap 4.4: expose alert failure count cho health check + admin monitoring.</summary>
    public long GetAlertFailureCount() => Interlocked.Read(ref _alertFailureCounter);

    public async Task<int> RecomputeBatchAsync(int batchSize, DateTime now, CancellationToken ct = default)
    {
        var userIds = await _riskRepo.GetAllActiveUserIdsAsync(batchSize, 0);
        var processed = 0;
        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await RecomputeForUserAsync(userId, now, ct);
                processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // GAP-R6-RT-NEW fix v5: app shutdown / job tick bị huỷ giữa SaveChanges
                // (Npgsql connection pool bị dispose). Đây là behavior bình thường
                // khi host shutdown, KHÔNG phải lỗi logic. Log info + dừng batch.
                _logger.LogInformation(
                    "RecomputeBatchAsync cancelled (likely app shutdown). Processed={Processed}.",
                    processed);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RecomputeForUser failed userId={UserId}", userId);
            }
        }
        return processed;
    }

    public Task<PlayerRiskScore?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _riskRepo.GetByUserIdAsync(userId, cancellationToken);

    public Task<IReadOnlyList<RiskScoreHistory>> GetHistoryAsync(Guid userId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default) =>
        _riskRepo.GetHistoryByUserIdAndDateRangeAsync(userId, fromDate, toDate, cancellationToken);

    /// <summary>
    /// BR-RISK-01 — Collect all signals from existing data sources.
    /// Full 10 signals: SIG-01..SIG-10.
    /// </summary>
    /// <summary>
    /// Gap 4.7 (2026-10-02): BR-RISK-04 mapping riskScore → AccountStatus.
    /// Banned/Suspended KHÔNG được set bởi auto-recompute (chỉ admin manual).
    /// </summary>
    private static AccountStatus MapRiskScoreToAccountStatus(int riskScore)
    {
        if (riskScore >= 75) return AccountStatus.Restricted;
        if (riskScore >= 50) return AccountStatus.Restricted;
        if (riskScore >= 30) return AccountStatus.Warning;
        return AccountStatus.Active;
    }

    /// <summary>
    /// Gap 4.7 (2026-10-02): kiểm tra accountStatus có phải upgrade không (numeric order).
    /// Active (1) < Warning (2) < Restricted (3) < Suspended (4) < Banned (5).
    /// </summary>
    private static bool IsAccountStatusUpgrade(AccountStatus current, AccountStatus next)
    {
        var currentOrder = (int)current;
        var nextOrder = (int)next;
        return nextOrder > currentOrder;
    }

    private async Task<Dictionary<string, int>> CollectSignalsAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var signals = new Dictionary<string, int>();

        var sevenDayWindow = now.AddDays(-7);
        var thirtyDayWindow = now.AddDays(-30);
        var oneDayWindow = now.AddDays(-1);

        // SIG-01: lobby TimeoutFailed trong 7d.
        signals["SIG-01"] = await _lobbyRepo.CountFailuresByTypeForHostAsync(
            userId, sevenDayWindow, now, LobbyStatus.TimeoutFailed);

        // SIG-02: lobby HostCancelled + Dissolved trong 7d (host chủ động hủy sau grace).
        var hostCancelled = await _lobbyRepo.CountFailuresByTypeForHostAsync(
            userId, sevenDayWindow, now, LobbyStatus.HostCancelled);
        var dissolved = await _lobbyRepo.CountFailuresByTypeForHostAsync(
            userId, sevenDayWindow, now, LobbyStatus.Dissolved);
        signals["SIG-02"] = hostCancelled + dissolved;

        // SIG-03: tổng BVC forfeit trong 30d (÷1000).
        var forfeitAmount = await _ledgerRepo.SumForfeitAsync(userId, thirtyDayWindow);
        signals["SIG-03"] = (int)forfeitAmount;

        // SIG-04: số playDate trong 30d có ≥ 2 lobby tạo+hủy (BR-NEW-05 spam pattern).
        signals["SIG-04"] = await _lobbyRepo.CountDistinctPlayDatesWithManyCreatesCancelsAsync(
            userId, thirtyDayWindow);

        // SIG-05: join/rời lobby trong 24 giờ.
        signals["SIG-05"] = await _lobbyRepo.CountJoinLeaveInWindowAsync(
            userId, oneDayWindow);

        // SIG-06: bị từ chối tạo lobby trong 30d.
        signals["SIG-06"] = await _lobbyRepo.CountDeniedCreateAttemptsAsync(
            userId, thirtyDayWindow);

        // SIG-08: create + cancel trong < 5 phút.
        signals["SIG-08"] = await _lobbyRepo.CountQuickCreateCancelAsync(
            userId, thirtyDayWindow, TimeSpan.FromMinutes(5));

        // SIG-09: chênh lệch giờ hoạt động vs baseline.
        // Gap 4.5 (2026-10-02): hard-code "not applicable MVP" hiện tại — Phase 2 sẽ implement.
        // Tracking ticket: BOARDVERSE-1099 — collect player active hours từ ActiveSession.StartedAt
        // trong 30 ngày, so sánh với baseline 8h-23h local time. Outlier (player chỉ chơi 2h-4h sáng)
        // → signal. Hiện tại để trống (không add signal) cho MVP.
        // signals["SIG-09"] = await ComputeActiveHoursDeviationAsync(userId, thirtyDayWindow, ct);

        // SIG-10: số lần bị report từ user khác trong 30d.
        signals["SIG-10"] = await _db.FriendReports
            .AsNoTracking()
            .CountAsync(r => r.TargetUserId == userId && r.CreatedAt >= thirtyDayWindow, ct);

        return signals;
    }
}
