namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái của BVC top-up request. Lifecycle:
///   Pending → Paid (webhook success) → terminal
///   Pending → Expired (qua 30 phút) → terminal
///   Pending → Failed (webhook failed/cancelled hoặc gateway error) → terminal
/// </summary>
public enum BvcTopUpStatus
{
    /// <summary>Mới tạo, chờ SePay webhook.</summary>
    Pending = 0,

    /// <summary>Webhook success → đã cộng BVC vào ví.</summary>
    Paid = 1,

    /// <summary>Webhook failed/cancelled.</summary>
    Failed = 2,

    /// <summary>Quá thời hạn (mặc định 30 phút) mà chưa có webhook.</summary>
    Expired = 3
}
