namespace BoardVerse.Core.Enum;

/// <summary>
/// Chế độ sử dụng deposit của Host khi thanh toán session.
/// Theo docs/design/host-deposit-discount-and-bvc-payment-design.md §B1.2 + BR-15 (modified).
///
/// Host có 3 lựa chọn lúc POS Pay:
/// - <see cref="None"/>: Không dùng deposit. Pay full session price.
/// - <see cref="DiscountGroup"/>: Deposit được phân bổ theo % thời gian cho tất cả active members.
/// - <see cref="DiscountHostOnly"/>: Deposit chỉ giảm bill của host (1 người).
/// </summary>
/// <remarks>
/// Số enum KHÔNG ĐƯỢC thay đổi trong tương lai (đã có data ở DB).
/// Chỉ được thêm giá trị mới ở cuối.
/// </remarks>
public enum HostDepositUsageMode
{
    /// <summary>Không dùng deposit làm discount (backward compatible với BR-09 cũ).</summary>
    None = 0,

    /// <summary>
    /// Deposit phân bổ theo % thời gian chơi cho tất cả active members (LeftAt == null HOẶC LeftAt > payTime).
    /// Mỗi member được giảm: totalDeposit × (member.TotalMinutesPlayed / session.TotalMinutes).
    /// </summary>
    DiscountGroup = 1,

    /// <summary>
    /// Deposit chỉ giảm bill của host (member có IsHost = true).
    /// Các members khác vẫn trả 100% tiền giờ.
    /// </summary>
    DiscountHostOnly = 2
}