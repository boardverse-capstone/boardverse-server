using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// Request để release cooling-off cho một user.
/// </summary>
public class ReleaseCoolingOffRequestDto
{
    [Required(ErrorMessage = ApiErrorMessages.Validation.ReasonRequired)]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = ApiErrorMessages.Validation.ReasonLength5To1000)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Response sau khi release cooling-off.
/// </summary>
public class ReleaseCoolingOffResponseDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool WasCoolingOff { get; set; }
    public DateTime? PreviousCoolingOffExpiresAt { get; set; }
    public string ReleaseReason { get; set; } = string.Empty;
    public Guid ReleasedBy { get; set; }
    public DateTime ReleasedAt { get; set; }
}

/// <summary>
/// DTO cho user đang trong trạng thái cooling-off.
/// </summary>
public class CoolingOffUserDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsCoolingOff { get; set; }
    public DateTime? CoolingOffExpiresAt { get; set; }
    public int CoolingOffDaysRemaining { get; set; }
    public int FailedLobbiesInWeek { get; set; }
    public int CancelledLobbiesInWeek { get; set; }
    public long TotalForfeitedBvc { get; set; }
    public DateTime? CoolingOffStartedAt { get; set; }
}

/// <summary>
/// BR-NEW-10 §XI.2 — Admin manually extend cooling-off cho 1 user.
/// Dùng khi cần gia hạn thêm (escalate hoặc customer support edge case).
/// </summary>
public class ExtendCoolingOffRequestDto
{
    /// <summary>Số ngày gia hạn thêm (1..90).</summary>
    [Required, Range(1, 90, ErrorMessage = "additionalDays phải trong khoảng 1..90.")]
    public int AdditionalDays { get; set; }

    /// <summary>Lý do extend (tối thiểu 10 ký tự, ghi audit log).</summary>
    [Required, StringLength(1000, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Response cho extend cooling-off.
/// </summary>
public class ExtendCoolingOffResponseDto
{
    public Guid UserId { get; set; }
    public DateTime? PreviousExpiresAt { get; set; }
    public DateTime NewExpiresAt { get; set; }
    public int AdditionalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid ExtendedBy { get; set; }
    public DateTime ExtendedAt { get; set; }
}

/// <summary>
/// BR-RISK-09 — Admin view player risk details.
/// Trả về riskScore, signals, cooling-off status. Admin-only.
/// User bình thường KHÔNG được thấy các field này (chỉ thấy RiskLevel).
/// </summary>
public class PlayerRiskDetailDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>RiskScore 0-100 (BR-RISK-01).</summary>
    public int RiskScore { get; set; }

    /// <summary>RiskLevel: low / medium / high / critical.</summary>
    public string RiskLevel { get; set; } = "low";

    /// <summary>RiskMultiplier: 1.0..2.0 (BR-RISK-03).</summary>
    public decimal RiskMultiplier { get; set; }

    /// <summary>AccountStatus (BR-RISK-04).</summary>
    public string AccountStatus { get; set; } = "active";

    /// <summary>Cooling-off active flag + expiry.</summary>
    public bool IsCoolingOff { get; set; }
    public DateTime? CoolingOffExpiresAt { get; set; }

    /// <summary>Signals (BR-RISK-01) cho admin — JSON snapshot từ signals gần nhất.</summary>
    public Dictionary<string, int> Signals { get; set; } = new();

    /// <summary>Số action history của user (BR-RISK-05/06 audit).</summary>
    public int ActionHistoryCount { get; set; }

    public DateTime LastUpdated { get; set; }
}
