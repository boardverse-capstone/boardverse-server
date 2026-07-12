namespace BoardVerse.Core.Enum;

/// <summary>
/// Chính sách hoàn cọc khi hủy booking.
/// Theo boardverse-state-machine.mdc - Section 1.
/// </summary>
public enum DepositRefundPolicy
{
    /// <summary>Hoàn 100% tiền cọc.</summary>
    Full = 0,

    /// <summary>Hoàn một phần theo chính sách thời gian của quán.</summary>
    Partial = 1,

    /// <summary>Tịch thu toàn bộ tiền cọc.</summary>
    None = 2
}
