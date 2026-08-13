using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;

namespace BoardVerse.Services.IServices;

/// <summary>
/// R-01 (BR-RISK-02): Service cho PlayerAlert — admin đọc + acknowledge + resolve.
/// </summary>
public interface IPlayerAlertService
{
    Task<PaginatedResponse<PlayerAlertDto>> GetPagedAsync(PlayerAlertQuery query);

    Task<PlayerAlertDto> AcknowledgeAsync(Guid alertId, Guid adminUserId);

    Task<PlayerAlertDto> ResolveAsync(Guid alertId, Guid adminUserId, string note);

    Task<PlayerAlertDto> DismissAsync(Guid alertId, Guid adminUserId, string note);

    /// <summary>Signal-driven create (gọi từ risk score recompute job).</summary>
    Task EnsureAlertForSignalsAsync(
        Guid userId,
        int riskScore,
        RiskLevel newLevel,
        RiskLevel previousLevel,
        string? signalsJson);

    /// <summary>Alert dashboard metrics.</summary>
    Task<PlayerAlertMetricsDto> GetMetricsAsync();

    Task<int> DismissStaleAlertsAsync(int maxAgeDays, int batchSize);
}

public class PlayerAlertMetricsDto
{
    public int Total { get; set; }
    public int OpenCritical { get; set; }
    public int Open { get; set; }
    public int Acknowledged { get; set; }
    public int Resolved { get; set; }
    public int Dismissed { get; set; }
}
