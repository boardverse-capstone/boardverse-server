namespace BoardVerse.Core.Enum;

/// <summary>
/// Loại event trong Transactional Outbox (BR-REQUIRED §17.5).
/// Worker sẽ poll outbox table → publish theo EventType.
/// </summary>
public enum OutboxEventType
{
    /// <summary>Lobby đã được tạo thành công + atomic hold BVC/seat/game. Cần publish cho discovery/notification.</summary>
    LobbyActivated = 0,

    /// <summary>Reservation đã giữ chỗ (seat + game copy + BVC). Confirmation UI cần update.</summary>
    ReservationHeld = 1,

    /// <summary>BVC đã bị hold vào heldBalance. Wallet UI cần update.</summary>
    DepositHeld = 2,

    /// <summary>Lobby đạt minPlayers/maxPlayers → booking confirmed. Notification host + members.</summary>
    LobbyConfirmed = 3,

    /// <summary>Lobby quá recruitmentDeadline mà chưa đủ người → timeoutFailed, hoàn 100% BVC.</summary>
    LobbyTimeout = 4,

    /// <summary>Lobby bị cafe từ chối duyệt (BR-NEW-11).</summary>
    LobbyRejectedByCafe = 5,

    /// <summary>Cafe không duyệt lobby trong 24h → expiredByCafe, hoàn 100% BVC.</summary>
    LobbyExpiredByCafe = 6,

    /// <summary>Host hủy lobby (BR-REFUND-02/03) → refund theo policy.</summary>
    LobbyCancelledByHost = 7,

    /// <summary>Cafe hủy lobby (BR-REFUND-04) → hoàn 100% BVC, không bồi thường.</summary>
    LobbyCancelledByCafe = 8,

    /// <summary>Booking no-show sau scheduledTime + gracePeriod (BR-21A.9) → forfeit deposit.</summary>
    LobbyNoShow = 9,

    /// <summary>POS check-in thành công → capture BVC deposit về doanh thu quán (BR-REVENUE-01).</summary>
    LobbyCheckedIn = 10,

    /// <summary>
    /// BR §21A.8 + BR-REVENUE-01: POS đóng phiên (ActiveSession → Paid) → capture BVC đã giữ
    /// về doanh thu quán, Reservation.Status = Completed, giải phóng seat + game inventory.
    /// </summary>
    SessionCompleted = 11,

    /// <summary>Deposit đã được hoàn về ví user (timeout, cancel, cafe hủy).</summary>
    DepositReleased = 12,

    /// <summary>Deposit đã được capture về doanh thu quán (check-in hoặc settlement).</summary>
    DepositCaptured = 13
}