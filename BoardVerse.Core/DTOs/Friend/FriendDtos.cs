using System.ComponentModel.DataAnnotations;

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
    /// Quan hệ bạn bè hiện tại giữa current user và user này.
    /// Null = chưa có quan hệ; "Pending" = đã gửi/nhận; "Accepted" = bạn bè; "Blocked" = đã chặn.
    /// </summary>
    public string? FriendshipStatus { get; set; }

    /// <summary>Số bạn chung giữa current user và user này.</summary>
    public int MutualFriendsCount { get; set; }
}
