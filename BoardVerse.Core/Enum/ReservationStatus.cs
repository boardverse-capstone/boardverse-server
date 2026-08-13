namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái vòng đợi của một Reservation (§6.1 + docs/time-slot-fixed-end-design (1).md §2.2).
/// Được tạo atomically cùng lobby khi đặt cọc BVC thành công.
/// </summary>
/// <remarks>
/// State machine:
/// <code>
/// AwaitingDeposit → Holding → Confirmed → CheckedIn → InProgress → Completed
///                                       ↘ Expired ↘ NoShow ↘ EarlyCheckout
///                                       ↘ CancelledByPlayer / CancelledByCafe
/// </code>
/// </remarks>
public enum ReservationStatus
{
    /// <summary>Đã tạo row, đang chờ ledger ghi nhận cuối cùng (không bao giờ persist).</summary>
    [Obsolete("Dùng AwaitingDeposit hoặc Holding. Draft chỉ còn cho backward-compat.")]
    Draft = 0,

    /// <summary>BR-RES-07 §2.2: Quote đã tạo, chờ confirm (chưa trừ BVC). Set sau <c>CreateQuoteAsync</c>.</summary>
    AwaitingDeposit = 1,

    /// <summary>Đã giữ BVC + ghế + game copy, lobby đang tuyển người.</summary>
    Holding = 2,

    /// <summary>Lobby đạt minPlayers trước recruitmentDeadline → confirmed.</summary>
    Confirmed = 3,

    /// <summary>POS đã quét QR check-in thành công.</summary>
    CheckedIn = 4,

    /// <summary>BR-END-01 §2.2: Đang chơi (ActiveSession.ACTIVE).</summary>
    InProgress = 5,

    /// <summary>Đã hoàn tất phiên chơi đúng giờ, capture deposit về doanh thu quán.</summary>
    Completed = 6,

    /// <summary>BR-END-04 §2.2: Player về sớm (ActiveSession.PAID sớm), refund 30% nếu playedRatio ≥ 50%.</summary>
    EarlyCheckout = 7,

    /// <summary>Đến recruitmentDeadline mà chưa đủ người (timeout failed).</summary>
    Expired = 8,

    /// <summary>Host tự hủy — hoàn 1 phần / 0% BVC theo BR-REFUND-02.</summary>
    CancelledByPlayer = 9,

    /// <summary>Quán hủy (BR-REFUND-04) — hoàn 100% BVC, không phạt.</summary>
    CancelledByCafe = 10,

    /// <summary>Host không đến sau scheduledTime + grace → forfeit deposit (BR §21A.9).</summary>
    NoShow = 11
}