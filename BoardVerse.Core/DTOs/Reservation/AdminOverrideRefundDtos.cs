using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Reservation;

/// <summary>
/// Admin override refund amount cho một reservation đã completed.
/// BR-REFUND-07: Staff override đặc biệt khi player dispute.
/// Ghi audit log PlayerActionHistory.
/// </summary>
public class AdminOverrideRefundRequestDto
{
    /// <summary>
    /// Số BVC admin cho hoàn (0 = không hoàn gì).
    /// Thường là một phần của DepositAmount đã capture.
    /// </summary>
    [Range(0, 10_000_000, ErrorMessage = "Số BVC phải từ 0 đến 10.000.000.")]
    public long RefundAmountBvc { get; set; }

    /// <summary>
    /// Ghi chú bắt buộc — lý do override (BR-RISK-05 audit).
    /// </summary>
    [Required]
    [StringLength(2000, MinimumLength = 5, ErrorMessage = "Ghi chú phải từ 5 đến 2000 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Kết quả override refund.
/// </summary>
public class AdminOverrideRefundResultDto
{
    public Guid ReservationId { get; set; }
    public Guid UserId { get; set; }
    public long OriginalDepositAmount { get; set; }
    public long PreviouslyCapturedAmount { get; set; }
    public long PreviouslyRefundedAmount { get; set; }
    public long NewRefundAmount { get; set; }
    public long ActualRefundAmount { get; set; }
    public Guid AdminUserId { get; set; }
    public DateTime ProcessedAt { get; set; }
}
