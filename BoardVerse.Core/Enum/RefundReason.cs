namespace BoardVerse.Core.Enum;

/// <summary>
/// Lý do refund (docs/time-slot-fixed-end-design (1).md §3.4 + §9.4 BR-REFUND-01..07).
/// Lưu trên <c>RefundTransaction.Reason</c>.
/// </summary>
public enum RefundReason
{
    /// <summary>BR-REFUND-01: Host hủy ≥ 24 giờ trước giờ chơi → refund 100%.</summary>
    CancelBefore24h,

    /// <summary>BR-REFUND-02: Host hủy &lt; 24 giờ trước giờ chơi → refund 0% (forfeit 100%).</summary>
    CancelAfter24h,

    /// <summary>BR-REFUND-03: Host hủy trong grace 15 phút + chưa có member → refund 100%.</summary>
    CancelGracePeriod,

    /// <summary>BR-REFUND-04/05/06: Player về sớm (EarlyLeave). Refund 0% / 30% / 0% theo playedRatio.</summary>
    EarlyCheckout,

    /// <summary>BR-REFUND-03 + BR-CHECKIN-02: Không check-in sau grace 30 phút → refund 0% (forfeit 100%).</summary>
    NoShow,

    /// <summary>BR-REFUND-07 + EC-02: Staff override do lỗi kỹ thuật.</summary>
    TechnicalIssue,

    /// <summary>BR-REFUND-07 + EC-02: Staff override do khẩn cấp.</summary>
    Emergency,

    /// <summary>BR-REFUND-07: Staff override cho các trường hợp khác.</summary>
    StaffOverride,

    /// <summary>Lý do không xác định.</summary>
    Other,

    /// <summary>BR-END-03: Kết thúc đúng giờ (playedRatio ≥ 90%) — không refund, forfeit toàn bộ.</summary>
    OnTime
}