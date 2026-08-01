namespace BoardVerse.Core.Enum;

/// <summary>
/// Chính sách hoàn cọc khi booking bị hủy (BR-18).
/// Manager cấu hình trên cafe qua PATCH /api/cafes/{id}/deposit-refund-policy (task #12).
/// </summary>
public enum DepositRefundPolicy
{
    /// <summary>Hoàn 100% cọc khi hủy.</summary>
    Full = 0,

    /// <summary>Hoàn theo bậc thang % theo thời gian trước giờ hẹn.</summary>
    Partial = 1,

    /// <summary>Không hoàn, cọc bị tịch thu về BoardVerse.</summary>
    None = 2
}
