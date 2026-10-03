using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// W-07: Breakdown số tiền + số lượng settlement theo từng trạng thái.
/// Dùng trong daily summary để admin thấy tổng quan nhanh Pending/Failed/Succeeded.
/// </summary>
public class SettlementStatusBreakdownDto
{
    public CafeSettlementStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalDepositAmount { get; set; }
    public decimal TotalNetTransferAmount { get; set; }
    public int Count { get; set; }
    public DateTime? LatestAt { get; set; }
}
