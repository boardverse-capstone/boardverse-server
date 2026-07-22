namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái lời mời tham gia lobby.
/// </summary>
public enum LobbyInviteStatus
{
    /// <summary>Đã gửi, chờ đối phương accept/decline.</summary>
    Pending = 0,

    /// <summary>Đã được accept, user có thể join lobby.</summary>
    Accepted = 1,

    /// <summary>Người nhận từ chối.</summary>
    Declined = 2,

    /// <summary>Lobby đã đầy hoặc đóng khi user chưa phản hồi.</summary>
    Expired = 3,

    /// <summary>Người gửi đã hủy lời mời.</summary>
    Cancelled = 4
}