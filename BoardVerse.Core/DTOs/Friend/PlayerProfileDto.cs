namespace BoardVerse.Core.DTOs.Friend;

/// <summary>
/// Thông tin chi tiết public của 1 player, lấy từ context Friends (kèm quan hệ + mutual friends).
/// Dùng khi user muốn xem trang cá nhân của người khác trước khi kết bạn / report.
/// </summary>
public class PlayerProfileDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? AvatarBorderUrl { get; set; }
    public string? Bio { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // Gamer stats
    public int GlobalElo { get; set; }
    public int KarmaPoints { get; set; }
    public string GamerTier { get; set; } = "Bronze";
    public int Level { get; set; }

    // Social stats
    public int FriendsCount { get; set; }
    public int MutualFriendsCount { get; set; }

    // Activity
    public string ActivityStatus { get; set; } = "Offline";
    public DateTime? LastActiveAt { get; set; }

    // Account signal
    public DateTime JoinedAt { get; set; }

    // Quan hệ giữa current user và player này
    public RelationshipDto Relationship { get; set; } = new();

    // Permissions cho UI
    public bool CanSendFriendRequest { get; set; }
    public bool CanReport { get; set; }
}

/// <summary>
/// Trạng thái quan hệ giữa current user và target user.
/// </summary>
public class RelationshipDto
{
    /// <summary>
    /// None / PendingSent / PendingReceived / Accepted / BlockedByMe / BlockedByThem.
    /// </summary>
    public string Status { get; set; } = "None";

    public Guid? FriendshipId { get; set; }
    public bool IsRequester { get; set; }
    public DateTime? FriendsSince { get; set; }
    public string? Message { get; set; }
}
