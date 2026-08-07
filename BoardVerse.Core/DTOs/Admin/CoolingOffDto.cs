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
