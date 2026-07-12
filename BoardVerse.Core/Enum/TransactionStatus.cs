namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái transaction từ payment gateway.
/// Dùng cho BookingDeposit và các payment khác.
/// </summary>
public enum TransactionStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
    Released = 4
}
