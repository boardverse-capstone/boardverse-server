namespace BoardVerse.Core.Enum;

/// <summary>
/// Loại entry ghi vào sổ cái BVC (BoardVerse Coin).
/// Theo BR § III.2 — Vòng đời BVC.
/// Append-only: mỗi entry ghi một lần, không UPDATE/DELETE.
///
/// Số enum KHÔNG ĐƯỢC thay đổi trong tương lai (đã có data ở DB).
/// Chỉ được thêm giá trị mới ở cuối.
/// </summary>
public enum LedgerEntryType
{
    /// <summary>Top-up tiền thật → BVC. availableBalance += amount.</summary>
    TopUp = 0,

    /// <summary>Đặt cọc giữ chỗ: availableBalance -= amount; heldBalance += amount.</summary>
    DepositHold = 1,

    /// <summary>Hoàn cọc (timeout / hủy lobby trước hạn): heldBalance -= amount; availableBalance += amount.</summary>
    DepositRelease = 2,

    /// <summary>Capture cọc về doanh thu quán (khi check-in / quá hạn giữ chỗ): heldBalance -= amount.</summary>
    DepositCapture = 3,

    /// <summary>Tịch thu cọc (no-show): heldBalance -= amount.</summary>
    DepositForfeit = 4,

    /// <summary>Sửa sai — luôn tạo entry mới, không sửa entry cũ.</summary>
    Adjustment = 5,

    /// <summary>Admin/support tặng BVC thủ công (compensation, manual credit). availableBalance += amount.</summary>
    AdminCredit = 6,

    /// <summary>Admin/support trừ BVC thủ công (penalty, refund manual). availableBalance -= amount.</summary>
    AdminDebit = 7
}
