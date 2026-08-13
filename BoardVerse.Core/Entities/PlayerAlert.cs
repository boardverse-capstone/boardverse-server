using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// R-01 (BR-RISK-02): Alert khi user vượt ngưỡng riskScore 30/50/75 HOẶC multi-account detected.
/// Admin phải review trong 24h cho Critical alert.
/// </summary>
public class PlayerAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User bị phát hiện có tín hiệu rủi ro.</summary>
    public Guid UserId { get; set; }

    public PlayerAlertType AlertType { get; set; }

    public PlayerAlertSeverity Severity { get; set; }

    /// <summary>
    /// Danh sách signal IDs (SIG-01..SIG-10) trigger alert, lưu JSON.
    /// Ví dụ: "[\"SIG-04\",\"SIG-08\"]"
    /// </summary>
    public string? Signals { get; set; }

    /// <summary>RiskScore tại thời điểm tạo alert (snapshot).</summary>
    public int RiskScoreSnapshot { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Admin userId đã acknowledge.</summary>
    public Guid? AcknowledgedBy { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    public PlayerAlertStatus Status { get; set; } = PlayerAlertStatus.Open;

    /// <summary>Ghi chú resolve/dismiss.</summary>
    public string? ResolutionNote { get; set; }
}
