using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Friend;

/// <summary>
/// Lời nhắn kèm theo lời mời kết bạn (override message trong FriendDtos).
/// </summary>
public class SendFriendRequestWithMessageDto
{
    [Required]
    public Guid AddresseeId { get; set; }

    [MaxLength(200)]
    public string? Message { get; set; }
}

/// <summary>
/// Friend list với trạng thái hoạt động + mutual friend count.
/// </summary>
public class FriendActivityDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int KarmaPoints { get; set; }
    public string? GamerTier { get; set; }
    public DateTime? LastActiveAt { get; set; }

    /// <summary>Trạng thái: Online (≤5 phút), RecentlyActive (≤1 giờ), Away (>1 giờ), Offline (chưa từng).</summary>
    public string ActivityStatus { get; set; } = "Offline";

    public DateTime FriendsSince { get; set; }
}

/// <summary>
/// Gợi ý kết bạn (chơi chung lobby cùng cafe, friend-of-friend).
/// </summary>
public class FriendSuggestionDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int KarmaPoints { get; set; }
    public string? GamerTier { get; set; }
    public int MutualFriendsCount { get; set; }

    /// <summary>Lý do gợi ý: MutualFriends / SameCafe / SameGameTemplate.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Bạn chung giữa 2 user.
/// </summary>
public class MutualFriendDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime FriendsSince { get; set; }
}

/// <summary>
/// Update privacy settings cho friend list.
/// </summary>
public class UpdateFriendPrivacyDto
{
    public bool? IsFriendListPublic { get; set; }

    /// <summary>Everyone / FriendsOfFriends.</summary>
    public string? AcceptFriendRequestsFrom { get; set; }

    /// <summary>0 = không giới hạn.</summary>
    [Range(0, 5000)]
    public int? FriendLimit { get; set; }
}

/// <summary>
/// Friend response với privacy + note preview.
/// </summary>
public class FriendDetailDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int KarmaPoints { get; set; }
    public string? GamerTier { get; set; }
    public DateTime FriendsSince { get; set; }
    public DateTime? LastActiveAt { get; set; }

    /// <summary>Alias riêng mà owner đặt cho friend này (nếu có).</summary>
    public string? Alias { get; set; }
    public string? Note { get; set; }
    public string? Tags { get; set; }

    public bool IsFriendListPublic { get; set; }
    public string AcceptFriendRequestsFrom { get; set; } = "Everyone";
    public int FriendLimit { get; set; }
}

/// <summary>
/// Create/Update friend note.
/// </summary>
public class UpsertFriendNoteDto
{
    [Required]
    [MaxLength(100)]
    public string Alias { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Note { get; set; }

    [MaxLength(200)]
    public string? Tags { get; set; }
}

/// <summary>
/// Friend note response.
/// </summary>
public class FriendNoteDto
{
    public Guid NoteId { get; set; }
    public Guid FriendUserId { get; set; }
    public string FriendUsername { get; set; } = string.Empty;
    public string? FriendAvatarUrl { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Báo cáo vi phạm friend.
/// </summary>
public class CreateFriendReportDto
{
    [Required]
    public Guid TargetUserId { get; set; }

    [Required]
    public string Category { get; set; } = "Other";

    [Required]
    [MinLength(5)]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Friend report response.
/// </summary>
public class FriendReportDto
{
    public Guid ReportId { get; set; }
    public Guid TargetUserId { get; set; }
    public string TargetUsername { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? AdminNote { get; set; }
}
