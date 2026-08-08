using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Admin resolve (approve/reject) một refund request.
/// BR-RISK-05: admin action ghi audit log + PlayerActionHistory.
/// </summary>
public class ResolveRefundRequestDto
{
    /// <summary>Quyết định của admin — Approve hoặc Reject.</summary>
    [Required]
    public RefundDecision Decision { get; set; }

    /// <summary>
    /// Số BVC admin duyệt (chỉ dùng khi Decision = Approve).
    /// Có thể khác <c>RequestedAmountBvc</c> — admin override khi cần.
    /// Null khi Decision = Reject.
    /// </summary>
    [Range(1, 10_000_000, ErrorMessage = "Số BVC phải trong khoảng 1 đến 10.000.000.")]
    public long? ApprovedAmountBvc { get; set; }

    /// <summary>Admin ghi chú (lý do duyệt/từ chối) — bắt buộc cho audit (BR-RISK-05).</summary>
    [Required]
    [StringLength(2000, MinimumLength = 5, ErrorMessage = "Ghi chú admin phải từ 5 đến 2000 ký tự.")]
    public string AdminNote { get; set; } = string.Empty;
}

public enum RefundDecision
{
    Approve = 1,
    Reject = 2
}