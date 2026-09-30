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
///                                       ↘ AbsorbedByMerge (staff POS merge)
/// </code>
/// <para>
/// <b>Phân biệt quan trọng (BR-MERGE-01):</b> <see cref="AbsorbedByMerge"/> KHÔNG
/// phải cancellation. Reservation bị staff POS merge (absorbed) vào reservation khác
/// vẫn giữ deposit held trong wallet — capture theo target session's policy khi kết
/// thúc. Tuyệt đối KHÔNG dùng <see cref="CancelledByPlayer"/> cho merge case.
/// </para>
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

    /// <summary>
    /// BR-REFUND-02: Host tự hủy reservation trước khi chơi. Trigger refund policy 1 phần / 0% BVC.
    /// <para>⚠️ KHÔNG dùng cho merge — dùng <see cref="AbsorbedByMerge"/>.</para>
    /// </summary>
    CancelledByPlayer = 9,

    /// <summary>Quán hủy (BR-REFUND-04) — hoàn 100% BVC, không phạt.</summary>
    CancelledByCafe = 10,

    /// <summary>Host không đến sau scheduledTime + grace → forfeit deposit (BR §21A.9).</summary>
    NoShow = 11,

    /// <summary>
    /// BR-MERGE-01: Source reservation bị staff POS merge (absorbed) vào target reservation.
    /// <para>
    /// Đây KHÔNG phải host cancellation. Host Nhóm A vẫn đang ở quán và tiếp tục chơi tại
    /// bàn mới (Nhóm B). Do đó deposit KHÔNG được refund, KHÔNG được forfeit — vẫn giữ
    /// held trong wallet và sẽ capture theo target session's policy khi bàn B kết thúc.
    /// </para>
    /// <para>
    /// LUÔN đi kèm <c>Reservation.SourceDissolved = true</c> và
    /// <c>Reservation.MergedIntoReservationId = target.ReservationId</c>.
    /// </para>
    /// <para>
    /// Đặt bởi <c>LobbyMergeService.ApproveMergeAsync</c> (Step 11) khi source lobby
    /// được dissolve sau khi transfer members sang target.
    /// </para>
    /// </summary>
    AbsorbedByMerge = 12
}
