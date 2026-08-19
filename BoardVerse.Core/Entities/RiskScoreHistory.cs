using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// BR-RISK-11: Lưu mỗi lần recompute riskScore — phục vụ audit 365 ngày + chart trend cho admin dashboard.
/// Không có partition trong MVP (single table); sau có thể partition theo SnapshotDate.
/// </summary>
public class RiskScoreHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public int RiskScore { get; set; }

    public RiskLevel RiskLevel { get; set; }

    /// <summary>JSON dictionary signal key → int value.</summary>
    public string? Signals { get; set; }

    /// <summary>SnapshotDate (DateOnly) — index cho retention query 365 ngày.</summary>
    public DateOnly SnapshotDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
