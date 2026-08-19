namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái tài khoản player (BR-RISK-04) — phạm vi áp dụng cho Wallet + Lobby/Booking.
/// Tách riêng <see cref="UserAccountStatus"/> (Active/Suspended/Banned) vì phạm vi
/// BR-RISK-04 yêu cầu 5 cấp độ chi tiết hơn cho luồng risk-score.
/// </summary>
public enum AccountStatus
{
    /// <summary>Hoạt động bình thường.</summary>
    Active = 0,

    /// <summary>Cảnh báo nhẹ (cọc ×2 theo BR-RISK-04). Vẫn tạo lobby được.</summary>
    Warning = 1,

    /// <summary>Bị hạn chế: KHÔNG tạo lobby, vẫn join / top-up được.</summary>
    Restricted = 2,

    /// <summary>Tạm khóa 7-30 ngày (do admin ADM-02/03). Tự hết hạn.</summary>
    Suspended = 3,

    /// <summary>Khóa vĩnh viễn (do admin ADM-04).</summary>
    Banned = 4
}
