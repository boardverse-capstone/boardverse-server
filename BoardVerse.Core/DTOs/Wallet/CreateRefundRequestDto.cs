using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Player tạo yêu cầu hoàn BVC — admin xem xét và duyệt.
/// BR-RISK-09: mọi admin resolve action ghi PlayerActionHistory.
/// BR § III.6: refund request lifecycle Pending → Approved/Rejected/Cancelled.
/// </summary>
public class CreateRefundRequestDto
{
    /// <summary>
    /// Id của ledger entry cần xem xét hoàn (mọi entry type — admin tự đánh giá).
    /// Player có thể lấy id từ <c>GET /api/v1/wallet/transactions</c>.
    /// </summary>
    [Required]
    public Guid RelatedLedgerEntryId { get; set; }

    /// <summary>Số BVC player yêu cầu hoàn (1 đến 10.000.000 BVC).</summary>
    [Required]
    [Range(1, 10_000_000, ErrorMessage = "Số BVC phải trong khoảng 1 đến 10.000.000.")]
    public long RequestedAmountBvc { get; set; }

    /// <summary>Lý do player gửi yêu cầu (min 20 ký tự, max 2000).</summary>
    [Required]
    [StringLength(2000, MinimumLength = 20, ErrorMessage = "Lý do phải từ 20 đến 2000 ký tự.")]
    public string PlayerReason { get; set; } = string.Empty;
}