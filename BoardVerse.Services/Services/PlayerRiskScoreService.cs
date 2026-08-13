using System.Text.Json;
using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Services.IServices;
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
            + signals.GetValueOrDefault("SIG-08", 0) * W_CreateCancelUnder5min;
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

        // BR-RISK-04: chỉ auto-update AccountStatus nếu nhân viên không set thủ công.
        // MVP đơn giản: chỉ update RiskScore, không tự động chuyển AccountStatus.
        // AccountStatus do admin set thủ công; job này không đụng.

        var previousLevel = snapshot.RiskLevel;

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
                _logger.LogError(ex, "PlayerAlertService.EnsureAlert failed for user {UserId}", userId);
            }
        }

        return snapshot;
    }

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "RecomputeForUser failed userId={UserId}", userId);
            }
        }
        return processed;
    }

    public Task<PlayerRiskScore?> GetCurrentAsync(Guid userId) =>
        _riskRepo.GetByUserIdAsync(userId);

    public Task<IReadOnlyList<RiskScoreHistory>> GetHistoryAsync(Guid userId, DateOnly fromDate, DateOnly toDate) =>
        _riskRepo.GetHistoryByUserIdAndDateRangeAsync(userId, fromDate, toDate);

    /// <summary>
    /// Thu thập signals từ các nguồn dữ liệu hiện có.
    /// MVP: 5 signals (SIG-01/02/03/04/08). BR-RISK-01 list full 10 signal nhưng 5 này cover được đa số spam.
    /// </summary>
    private async Task<Dictionary<string, int>> CollectSignalsAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var signals = new Dictionary<string, int>();

        var sevenDayWindow = now.AddDays(-7);
        var thirtyDayWindow = now.AddDays(-30);

        // SIG-01: lobby TimeoutFailed trong 7d.
        signals["SIG-01"] = await _lobbyRepo.CountFailuresByTypeForHostAsync(
            userId, sevenDayWindow, now, LobbyStatus.TimeoutFailed);

        // SIG-02: lobby HostCancelled trong 7d.
        signals["SIG-02"] = await _lobbyRepo.CountFailuresByTypeForHostAsync(
            userId, sevenDayWindow, now, LobbyStatus.HostCancelled);

        // SIG-03: tổng BVC forfeit trong 30d.
        var forfeitAmount = await _ledgerRepo.SumForfeitAsync(userId, thirtyDayWindow);
        signals["SIG-03"] = (int)forfeitAmount;

        // SIG-08: count các lobby create+cancel trong < 5 phút (trong 30d) — heuristic từ UpdatedAt - CreatedAt.
        signals["SIG-08"] = await _lobbyRepo.CountQuickCreateCancelAsync(userId, thirtyDayWindow, TimeSpan.FromMinutes(5));

        // SIG-04 để 0 — cần query phức tạp (group by PlayDate count >5) — thêm sau.
        signals["SIG-04"] = 0;

        return signals;
    }
}
