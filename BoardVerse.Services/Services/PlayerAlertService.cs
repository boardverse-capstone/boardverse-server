using System.Text.Json;
using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
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

/// <inheritdoc cref="IPlayerAlertService"/>
public class PlayerAlertService : IPlayerAlertService
{
    private const int AutoAlertCooldownHours = 24;

    private readonly IPlayerAlertRepository _alertRepo;
    private readonly BoardVerseDbContext _db;
    private readonly ILogger<PlayerAlertService> _logger;

    public PlayerAlertService(
        IPlayerAlertRepository alertRepo,
        BoardVerseDbContext db,
        ILogger<PlayerAlertService> logger)
    {
        _alertRepo = alertRepo;
        _db = db;
        _logger = logger;
    }

    public Task<PaginatedResponse<PlayerAlertDto>> GetPagedAsync(PlayerAlertQuery query, CancellationToken cancellationToken = default) =>
        _alertRepo.GetPagedAsync(query);

    public async Task<PlayerAlertDto> AcknowledgeAsync(Guid alertId, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepo.GetByIdAsync(alertId);
        if (alert == null)
        {
            throw new NotFoundException(ApiErrorMessages.AdminModeration.AlertNotFound(alertId));
        }

        if (alert.Status != PlayerAlertStatus.Open)
        {
            throw new ConflictException(ApiErrorMessages.AdminModeration.AlertAlreadyProcessed(alert.Status));
        }

        alert.Status = PlayerAlertStatus.Acknowledged;
        alert.AcknowledgedBy = adminUserId;
        alert.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ToDto(alert);
    }

    public async Task<PlayerAlertDto> ResolveAsync(Guid alertId, Guid adminUserId, string note, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepo.GetByIdAsync(alertId);
        if (alert == null)
        {
            throw new NotFoundException(ApiErrorMessages.AdminModeration.AlertNotFound(alertId));
        }

        if (alert.Status == PlayerAlertStatus.Resolved)
        {
            throw new ConflictException(ApiErrorMessages.AdminModeration.AlertAlreadyResolved);
        }

        alert.Status = PlayerAlertStatus.Resolved;
        alert.AcknowledgedBy ??= adminUserId;
        alert.AcknowledgedAt ??= DateTime.UtcNow;
        alert.ResolutionNote = note?.Trim();
        await _db.SaveChangesAsync();

        // BR-RISK-05: ghi PlayerActionHistory audit.
        _db.PlayerActionHistories.Add(new PlayerActionHistory
        {
            Id = Guid.NewGuid(),
            UserId = alert.UserId,
            ActionType = AdminActionType.Warning,
            ActionBy = adminUserId,
            Reason = $"Alert {alertId} resolved: {note?.Trim()}",
            Metadata = JsonSerializer.Serialize(new
            {
                alertId = alert.Id,
                alertType = alert.AlertType.ToString(),
                severity = alert.Severity.ToString(),
                riskScore = alert.RiskScoreSnapshot
            }),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return ToDto(alert);
    }

    public async Task<PlayerAlertDto> DismissAsync(Guid alertId, Guid adminUserId, string note, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepo.GetByIdAsync(alertId);
        if (alert == null)
        {
            throw new NotFoundException(ApiErrorMessages.AdminModeration.AlertNotFound(alertId));
        }

        alert.Status = PlayerAlertStatus.Dismissed;
        alert.AcknowledgedBy ??= adminUserId;
        alert.AcknowledgedAt ??= DateTime.UtcNow;
        alert.ResolutionNote = note?.Trim();
        await _db.SaveChangesAsync();

        _db.PlayerActionHistories.Add(new PlayerActionHistory
        {
            Id = Guid.NewGuid(),
            UserId = alert.UserId,
            ActionType = AdminActionType.Warning,
            ActionBy = adminUserId,
            Reason = $"Alert {alertId} dismissed: {note?.Trim()}",
            Metadata = JsonSerializer.Serialize(new
            {
                alertId = alert.Id,
                alertType = alert.AlertType.ToString(),
                dismissed = true
            }),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return ToDto(alert);
    }

    public async Task EnsureAlertForSignalsAsync(
        Guid userId,
        int riskScore,
        RiskLevel newLevel,
        RiskLevel previousLevel,
        string? signalsJson, CancellationToken cancellationToken = default)
    {
        // BR-RISK-02: chỉ trigger alert khi level tăng ≥ Medium.
        // MVP: chỉ trigger cho level Critical để tránh spam alerts.
        if (newLevel != RiskLevel.Critical) return;

        var severity = newLevel switch
        {
            RiskLevel.Critical => PlayerAlertSeverity.Critical,
            RiskLevel.High => PlayerAlertSeverity.Warning,
            _ => PlayerAlertSeverity.Info
        };

        var signalsKey = signalsJson ?? string.Empty;
        var shouldCreate = await _alertRepo.ShouldCreateAutoAlertAsync(
            userId, PlayerAlertType.AutoThresholdCrossed, signalsKey, AutoAlertCooldownHours);

        if (!shouldCreate) return;

        var alert = new PlayerAlert
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AlertType = PlayerAlertType.AutoThresholdCrossed,
            Severity = severity,
            Signals = signalsJson,
            RiskScoreSnapshot = riskScore,
            Status = PlayerAlertStatus.Open,
            CreatedAt = DateTime.UtcNow
        };

        await _alertRepo.AddAsync(alert);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "PlayerAlert created userId={UserId} riskScore={Score} signals={Signals}",
            userId, riskScore, signalsJson);
    }

    public async Task<PlayerAlertMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var alerts = await _db.PlayerAlerts.AsNoTracking().ToListAsync();
        return new PlayerAlertMetricsDto
        {
            Total = alerts.Count,
            OpenCritical = alerts.Count(a => a.Status == PlayerAlertStatus.Open && a.Severity == PlayerAlertSeverity.Critical),
            Open = alerts.Count(a => a.Status == PlayerAlertStatus.Open),
            Acknowledged = alerts.Count(a => a.Status == PlayerAlertStatus.Acknowledged),
            Resolved = alerts.Count(a => a.Status == PlayerAlertStatus.Resolved),
            Dismissed = alerts.Count(a => a.Status == PlayerAlertStatus.Dismissed)
        };
    }

    public async Task<int> DismissStaleAlertsAsync(int maxAgeDays, int batchSize)
    {
        var stale = await _alertRepo.GetStaleAlertsForDismissalAsync(maxAgeDays, batchSize);
        var now = DateTime.UtcNow;
        foreach (var alert in stale)
        {
            alert.Status = PlayerAlertStatus.Dismissed;
            alert.ResolutionNote = $"Auto-dismissed after {maxAgeDays} days without acknowledgement.";
            _db.PlayerActionHistories.Add(new PlayerActionHistory
            {
                Id = Guid.NewGuid(),
                UserId = alert.UserId,
                ActionType = AdminActionType.Warning,
                ActionBy = Guid.Empty, // system
                Reason = $"Alert {alert.Id} auto-dismissed (no action)",
                Metadata = JsonSerializer.Serialize(new
                {
                    alertId = alert.Id,
                    autoDismissed = true,
                    ageDays = (now - alert.CreatedAt).Days
                }),
                CreatedAt = now
            });
        }
        await _db.SaveChangesAsync();
        return stale.Count;
    }

    private static PlayerAlertDto ToDto(PlayerAlert a) => new()
    {
        Id = a.Id,
        UserId = a.UserId,
        Username = a.User?.Username,
        AlertType = a.AlertType,
        Severity = a.Severity,
        Status = a.Status,
        Signals = a.Signals,
        RiskScoreSnapshot = a.RiskScoreSnapshot,
        AcknowledgedBy = a.AcknowledgedBy,
        AcknowledgedAt = a.AcknowledgedAt,
        ResolutionNote = a.ResolutionNote,
        CreatedAt = a.CreatedAt
    };
}
