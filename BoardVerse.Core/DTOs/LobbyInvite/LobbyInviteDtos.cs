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