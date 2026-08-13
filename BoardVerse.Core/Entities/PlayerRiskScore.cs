using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// BR-RISK-01: Snapshot hiện tại của riskScore user. Mỗi user chỉ có 1 row (PK = UserId).
/// Được recompute mỗi giờ bởi <c>risk_score_recompute</c> job.
/// </summary>
public class PlayerRiskScore
{
    /// <summary>PK = UserId để đảm bảo 1 user chỉ có 1 snapshot.</summary>
    public Guid UserId { get; set; }

    /// <summary>0-100.</summary>
    public int RiskScore { get; set; }

    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

    /// <summary>JSON dictionary signal key → int value. VD: {"SIG-01": 3, "SIG-08": 12}.</summary>
    public string? Signals { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>Ghi chú nội bộ của admin.</summary>
    public string? AdminNote { get; set; }

    public Guid? AdminActionBy { get; set; }

    public DateTime? AdminActionAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
