using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// W-06: DTO trả về cho admin list settlement endpoint.
/// Dùng cho cả GET /api/v1/admin/settlements và GET /api/v1/admin/settlements/failed.
/// Đủ thông tin để admin xác định đúng settlement trước khi bấm retry/override — không thể
/// nhầm với reservationId/sessionId vì đã có SettlementId riêng + CafeName + Amount.
/// </summary>
public class SettlementListItemDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public string? CafeName { get; set; }
    public Guid CafeManagerId { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public Guid? BookingDepositId { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal? FeeAmount { get; set; }
    public decimal NetTransferAmount { get; set; }
    public string? SePayTransferId { get; set; }
    public CafeSettlementStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? TransferredAt { get; set; }
    public Guid? OverrideBy { get; set; }
    public DateTime? OverrideAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
