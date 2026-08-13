using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// Phase 4 / EC-11 (§7.2 doc <c>time-slot-fixed-end-design.md</c>):
/// Request để staff mở audit ticket khi player dispute played time.
/// Staff ghi claim của player, lưu vào <c>PlayerActionHistory</c> với <c>ActionType=PlayedTimeDisputed</c>.
/// </summary>
/// <remarks>
/// EC-11 rationale: POS logs (StartedAt scan QR timestamp + EndedAt POS button timestamp)
/// là evidence definitive (§21F.20, BR-REFUND-07). Endpoint này chỉ audit lại việc player
/// đã yêu cầu review — không tự động sửa hóa đơn.
/// </remarks>
public class DisputePlayedTimeRequestDto
{
    /// <summary>Mã phiên chơi bị player dispute.</summary>
    [Required]
    public Guid SessionId { get; set; }

    /// <summary>Player khiếu nại: đã đến sớm hơn / về muộn hơn / thời gian nghỉ.</summary>
    [Required, StringLength(64)]
    public string DisputeType { get; set; } = string.Empty;

    /// <summary>Lý do / mô tả chi tiết từ player.</summary>
    [Required, StringLength(1000, MinimumLength = 10)]
    public string PlayerClaim { get; set; } = string.Empty;

    /// <summary>Phương án staff propose (nếu có). Optional.</summary>
    [StringLength(500)]
    public string? ProposedResolution { get; set; }
}

/// <summary>
/// Response sau khi tạo audit log thành công.
/// </summary>
public class DisputePlayedTimeResponseDto
{
    public Guid AuditId { get; set; }
    public Guid SessionId { get; set; }
    public DateTime SessionStartedAt { get; set; }
    public DateTime? SessionEndedAt { get; set; }
    public int SessionTotalMinutes { get; set; }
    public string DisputeType { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Phase 5 / EC-11 — Manager override played time (BR-REFUND-07 §time-slot-fixed-end-design v3.0).
/// Manager review evidence từ POS logs + dispute audit (xem <see cref="DisputePlayedTimeRequestDto"/>),
/// sau đó chỉnh sửa <c>ActiveSession.TotalMinutesPlayed</c> + tính lại Subtotal/TotalAmount.
///
/// Trigger: <c>AdminActionType.PlayedTimeOverridden = 41</c>.
/// Quyền: Manager only (Staff chỉ được mở dispute, không override).
/// </summary>
/// <remarks>
/// <para>
/// Quy trình:
/// <list type="number">
///   <item><description>Staff mở dispute (Phase 4) → lưu audit log PlayedTimeDisputed.</description></item>
///   <item><description>Manager review evidence (StartedAt scan QR + EndedAt POS button).</description></item>
///   <item><description>Manager override bằng cách set <c>NewTotalMinutesPlayed</c> mới.</description></item>
///   <item><description>Service recalc Subtotal + TotalAmount, ghi audit PlayedTimeOverridden.</description></item>
/// </list>
/// </para>
/// <para>
/// **Quy tắc BR-REFUND-07**:
/// <list type="bullet">
///   <item><description><c>NewTotalMinutesPlayed</c> phải trong khoảng [0, PolicyMaxMinutes]. Default PolicyMax = 1440 (24h).</description></item>
///   <item><description>Phải có ít nhất 1 dispute audit (PlayedTimeDisputed) cho session trước khi cho override.</description></item>
///   <item><description>Manager không thể override session đã PAID (chỉ cho phép khi còn Unpaid/Active).</description></item>
/// </list>
/// </para>
/// </remarks>
public class OverridePlayedTimeRequestDto
{
    /// <summary>Mã phiên chơi cần override.</summary>
    [Required]
    public Guid SessionId { get; set; }

    /// <summary>
    /// Manager chỉ định tổng phút chơi mới (sau khi review evidence).
    /// Phải ≥ 0 và ≤ <c>PolicyMaxMinutes</c> (default 24h = 1440).
    /// </summary>
    [Required, Range(0, 1440, ErrorMessage = "NewTotalMinutesPlayed phải trong khoảng 0..1440 (24 giờ).")]
    public int NewTotalMinutesPlayed { get; set; }

    /// <summary>Lý do override (audit log bắt buộc, tối thiểu 20 ký tự).</summary>
    [Required, StringLength(1000, MinimumLength = 20)]
    public string OverrideReason { get; set; } = string.Empty;
}

/// <summary>
/// Response sau khi Manager override thành công.
/// </summary>
public class OverridePlayedTimeResponseDto
{
    public Guid OverrideAuditId { get; set; }
    public Guid SessionId { get; set; }
    public Guid DisputeAuditId { get; set; }

    /// <summary>TotalMinutesPlayed trước override (POS evidence).</summary>
    public int PreviousTotalMinutes { get; set; }

    /// <summary>TotalMinutesPlayed mới do Manager set.</summary>
    public int NewTotalMinutes { get; set; }

    /// <summary>Subtotal cũ (VND).</summary>
    public decimal PreviousSubtotal { get; set; }

    /// <summary>Subtotal mới (sau khi recalc theo NewTotalMinutes).</summary>
    public decimal NewSubtotal { get; set; }

    /// <summary>Subtotal chênh lệch (NewSubtotal - PreviousSubtotal). Có thể âm nếu Manager giảm.</summary>
    public decimal SubtotalDelta { get; set; }

    /// <summary>TotalAmount = NewSubtotal + PenaltyAmount.</summary>
    public decimal NewTotalAmount { get; set; }

    /// <summary>Tên policy áp dụng: "BR-REFUND-07 ManagerOverride".</summary>
    public string PolicyApplied { get; set; } = string.Empty;

    public string Status { get; set; } = "Overridden";
    public DateTime OverriddenAt { get; set; }
}