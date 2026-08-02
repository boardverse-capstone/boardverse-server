namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái vòng đời của một Reservation (§6.1).
/// Được tạo atomically cùng lobby khi đặt cọc BVC thành công.
/// </summary>
public enum ReservationStatus
{
    /// <summary>Đã tạo row, đang chờ ledger ghi nhận cuối cùng (không bao giờ persist).</summary>
    Draft = 0,

    /// <summary>Đã giữ BVC + ghế + game copy, lobby đang tuyển người.</summary>
    Holding = 1,

    /// <summary>Lobby đạt minPlayers trước recruitmentDeadline → confirmed.</summary>
    Confirmed = 2,

    /// <summary>Đến recruitmentDeadline mà chưa đủ người (timeout failed).</summary>
    Expired = 3,

    /// <summary>POS đã quét QR check-in thành công.</summary>
    CheckedIn = 4,

    /// <summary>Đã hoàn tất phiên chơi, capture deposit về doanh thu quán.</summary>
    Completed = 5,

    /// <summary>Host tự hủy — hoàn 1 phần / 0% BVC theo BR-REFUND-02.</summary>
    CancelledByPlayer = 6,

    /// <summary>Quán hủy (BR-REFUND-04) — hoàn 100% BVC, không phạt.</summary>
    CancelledByCafe = 7,

    /// <summary>Host không đến sau scheduledTime + grace → forfeit deposit (BR §21A.9).</summary>
    NoShow = 8
}
