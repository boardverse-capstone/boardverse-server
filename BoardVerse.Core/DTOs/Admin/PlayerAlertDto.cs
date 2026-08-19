using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// R-01: DTO trả về cho admin dashboard hoặc alert detail.
/// </summary>
public class PlayerAlertDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? Username { get; set; }
    public PlayerAlertType AlertType { get; set; }
    public PlayerAlertSeverity Severity { get; set; }
    public PlayerAlertStatus Status { get; set; }
    public string? Signals { get; set; }
    public int RiskScoreSnapshot { get; set; }
    public Guid? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime CreatedAt { get; set; }
}
