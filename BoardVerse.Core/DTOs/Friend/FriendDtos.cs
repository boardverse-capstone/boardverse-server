using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Friend;

/// <summary>
/// Body gửi lời mời kết bạn.
/// AddresseeId: người được mời; Message: lời nhắn kèm (optional).
/// </summary>
public class SendFriendRequestDto
{
    [Required]
    public Guid AddresseeId { get; set; }

    [MaxLength(200)]
    public string? Message { get; set; }
}

/// <summary>
/// Response thông tin quan hệ bạn bè giữa current user và user khác.
/// </summary>
public class FriendshipResponseDto
{
    public Guid FriendshipId { get; set; }
    public Guid OtherUserId { get; set; }
    public string OtherUsername { get; set; } = string.Empty;
    public string? OtherAvatarUrl { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Ai là người gửi lời mời (true nếu current user là requester).</summary>
    public bool IsRequester { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? Message { get; set; }
    public DateTime? AddresseeReadAt { get; set; }

    /// <summary>Số bạn chung giữa current user và other user (chỉ tính khi accepted).</summary>
    public int MutualFriendsCount { get; set; }
}

/// <summary>
/// Tóm tắt thông tin friend để render UI danh sách.
/// </summary>
public class FriendSummaryDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int KarmaPoints { get; set; }
    public string? GamerTier { get; set; }
    public DateTime FriendsSince { get; set; }

    /// <summary>Activity status: Online / RecentlyActive / Away / Offline.</summary>
    public string ActivityStatus { get; set; } = "Offline";
    public DateTime? LastActiveAt { get; set; }
}

/// <summary>
/// Kết quả tìm kiếm user để gửi friend request.
/// </summary>
public class UserSearchResultDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int KarmaPoints { get; set; }

    /// <summary>
    /// Trạng thái quan hệ thô từ DB (Pending / Accepted / Blocked / Removed).
    /// Chỉ dùng để debug hoặc backend xử lý — UI nên đọc <see cref="RelationshipDirection"/> thay thế.
    /// </summary>
    public string? FriendshipStatus { get; set; }

    /// <summary>
    /// Hướng quan hệ từ góc nhìn current user (BR-FRIEND-UI-DIRECTION-01).
    /// UI render theo direction này để hiển thị đúng nút bấm:
    /// - OutgoingRequest → "Đã gửi lời mời" (disable gửi lại)
    /// - IncomingRequest  → "Chấp nhận / Từ chối"
    /// - Accepted         → "Bạn bè" + nút nhắn/mời lobby
    /// - BlockedByMe      → "Bỏ chặn"
    /// - BlockedByThem    → Ẩn hoặc disable mọi action
    /// - None             → "Gửi lời mời kết bạn"
    /// </summary>
    public FriendshipRelationshipDirection RelationshipDirection { get; set; } = FriendshipRelationshipDirection.None;

    /// <summary>Số bạn chung giữa current user và user này.</summary>
    public int MutualFriendsCount { get; set; }
}
