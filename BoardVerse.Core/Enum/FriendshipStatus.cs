namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái quan hệ bạn bè giữa hai người dùng.
/// Mỗi Friendship record lưu theo cặp (RequesterId, AddresseeId) để tránh duplicate.
/// </summary>
public enum FriendshipStatus
{
    /// <summary>Đã gửi lời mời kết bạn, chờ đối phương accept.</summary>
    Pending = 0,

    /// <summary>Hai bên đã là bạn bè.</summary>
    Accepted = 1,

    /// <summary>Bị chặn (bên bị chặn không thể gửi lời mời/lobby invite mới).</summary>
    Blocked = 2,

    /// <summary>Một trong hai bên đã hủy kết bạn.</summary>
    Removed = 3
}