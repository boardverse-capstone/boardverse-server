using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Admin thay đổi AccountStatus của một user (lock/unlock).
/// Dùng cho BR-RISK-04, BR-RISK-05, BR-RISK-06.
/// </summary>
public class AdminSetStatusRequestDto
{
    /// <summary>UserId của player bị thay đổi trạng thái.</summary>
    [Required]
    public Guid TargetUserId { get; set; }

    /// <summary>Trạng thái mới: Active, Warning, Restricted, Suspended, Banned.</summary>
    [Required]
    public AccountStatus NewStatus { get; set; }

    /// <summary>Lý do thay đổi — bắt buộc cho audit (BR-RISK-05).</summary>
    [Required]
    [StringLength(512, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Thời gian khóa (nullable). Áp dụng khi NewStatus = Suspended.
    /// Nếu null và Status = Suspended → khóa vĩnh viễn (chỉ Senior admin được).
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Idempotency key do admin sinh (uuid v4).</summary>
    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Kết quả thay đổi AccountStatus.
/// </summary>
public class AdminSetStatusResultDto
{
    public Guid TargetUserId { get; set; }
    public AccountStatus PreviousStatus { get; set; }
    public AccountStatus NewStatus { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime ChangedAt { get; set; }
}
