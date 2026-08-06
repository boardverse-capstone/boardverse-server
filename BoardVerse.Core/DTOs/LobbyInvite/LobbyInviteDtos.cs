using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.LobbyInvite;

/// <summary>
/// Body gửi lời mời tham gia lobby cho một user.
/// Dùng cho cả public/private lobby (private bắt buộc phải qua invite).
/// </summary>
public class SendLobbyInviteRequestDto
{
    [Required]
    public Guid InviteeId { get; set; }

    [MaxLength(300)]
    public string? Message { get; set; }
}

/// <summary>
/// Response lời mời lobby.
/// </summary>
public class LobbyInviteResponseDto
{
    public Guid InviteId { get; set; }
    public Guid LobbyId { get; set; }
    public string? LobbyName { get; set; }
    public string? GameName { get; set; }
    public DateTime? ScheduledStartTime { get; set; }
    public Guid InviterId { get; set; }
    public string InviterUsername { get; set; } = string.Empty;
    public Guid InviteeId { get; set; }
    public string InviteeUsername { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Response join lobby bằng share code.
/// </summary>
public class JoinLobbyByShareCodeRequestDto
{
    [Required]
    [MaxLength(16)]
    public string ShareCode { get; set; } = string.Empty;
}

/// <summary>
/// Response chứa lobby ID + share code (để client copy &amp; share).
/// </summary>
public class LobbyShareInfoDto
{
    public Guid LobbyId { get; set; }
    public string ShareCode { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public string LobbyStatus { get; set; } = string.Empty;
}

/// <summary>
/// Query params cho <c>GET /lobbies/{lobbyId}/invitable-friends</c>.
/// Tất cả field optional.
/// </summary>
public class LobbyInvitableFriendsQuery
{
    /// <summary>Tìm friend theo username (case-insensitive contains). Áp dụng sau khi load friend list.</summary>
    public string? Search { get; set; }

    /// <summary>Chỉ lấy friend đang Online hoặc RecentlyActive.</summary>
    public bool OnlineOnly { get; set; }

    /// <summary>Lọc friend có KarmaPoints &gt;= giá trị này.</summary>
    public int? MinKarma { get; set; }

    /// <summary>
    /// Filter theo nhiều <see cref="LobbyInviteFriendStatus"/> (comma-separated).
    /// VD: <c>Invitable,InvitePending</c>.
    /// Nếu null/empty sẽ trả về tất cả status.
    /// Hữu ích cho UI hiển thị tab "Đã mời" (status=InvitePending) hoặc "Có thể mời" (status=Invitable).
    /// </summary>
    public string? Status { get; set; }

    /// <summary>Giới hạn kết quả (1-200, mặc định 100).</summary>
    public int Limit { get; set; } = 100;
}

/// <summary>
    /// Trạng thái mời của 1 friend đối với 1 lobby cụ thể.
    /// Dùng cho endpoint <c>GET /lobbies/{lobbyId}/invitable-friends</c> để client
    /// đổi nút UI cho phù hợp (Invite / Cancel / Sent / In Lobby / etc).
    /// </summary>
public enum LobbyInviteFriendStatus
{
    /// <summary>Chưa có quan hệ gì với lobby, có thể gửi invite.</summary>
    Invitable = 0,

    /// <summary>Đã gửi lời mời Pending cho friend này.</summary>
    InvitePending = 1,

    /// <summary>Friend đã accept lời mời (lịch sử, không còn pending).</summary>
    InviteAccepted = 2,

    /// <summary>Friend đã từ chối / hủy / hết hạn invite gần nhất.</summary>
    InviteNotPending = 3,

    /// <summary>Friend đã là thành viên active của lobby.</summary>
    AlreadyMember = 4,

    /// <summary>Friend đã block current user (không gửi invite được).</summary>
    BlockedByThem = 5,

    /// <summary>Current user đã block friend (không gửi invite được).</summary>
    BlockedByMe = 6,

    /// <summary>Lobby đã đóng / không còn nhận invite.</summary>
    LobbyClosed = 7
}

/// <summary>
/// Friend có thể mời vào lobby — kết quả trả về từ endpoint
/// <c>GET /api/v1/lobbies/{lobbyId}/invitable-friends</c>.
/// Bao gồm trạng thái quan hệ + cờ để client đổi UI mà không cần gọi thêm API.
/// </summary>
public class LobbyInvitableFriendDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int KarmaPoints { get; set; }
    public string? GamerTier { get; set; }

    /// <summary>Online / RecentlyActive / Away / Offline.</summary>
    public string ActivityStatus { get; set; } = "Offline";
    public DateTime? LastActiveAt { get; set; }

    /// <summary>FriendsSince với current user.</summary>
    public DateTime FriendsSince { get; set; }

    /// <summary>
    /// Trạng thái mời cho lobby hiện tại.
    /// Client đọc cờ này để quyết định nút:
    /// - <see cref="LobbyInviteFriendStatus.Invitable"/>     → hiển thị "Mời".
    /// - <see cref="LobbyInviteFriendStatus.InvitePending"/>  → hiển thị "Đã gửi / Hủy lời mời".
    /// - <see cref="LobbyInviteFriendStatus.AlreadyMember"/>  → disable, hiển thị "Đã trong phòng".
    /// - <see cref="LobbyInviteFriendStatus.LobbyClosed"/>    → disable, "Phòng đã đóng".
    /// - <see cref="LobbyInviteFriendStatus.BlockedByThem"/> / <see cref="LobbyInviteFriendStatus.BlockedByMe"/> → ẩn hoặc disable.
    /// </summary>
    public LobbyInviteFriendStatus InviteStatus { get; set; } = LobbyInviteFriendStatus.Invitable;

    /// <summary>InviteId của lời mời gần nhất (Pending nếu có) — null nếu không có.</summary>
    public Guid? LatestInviteId { get; set; }

    /// <summary>Trạng thái raw của invite gần nhất: Pending / Accepted / Declined / Expired / Cancelled.</summary>
    public string? LatestInviteStatus { get; set; }

    /// <summary>Cờ tiện cho client: đã ở trong lobby chưa?</summary>
    public bool IsInLobby => InviteStatus == LobbyInviteFriendStatus.AlreadyMember;

    /// <summary>Cờ tiện: đã gửi Pending invite chưa? (client đổi nút "Mời" → "Hủy lời mời").</summary>
    public bool HasPendingInvite => InviteStatus == LobbyInviteFriendStatus.InvitePending;

    /// <summary>Cờ tiện: bị block (cả 2 chiều) không thể mời.</summary>
    public bool IsBlocked =>
        InviteStatus == LobbyInviteFriendStatus.BlockedByMe ||
        InviteStatus == LobbyInviteFriendStatus.BlockedByThem;
}