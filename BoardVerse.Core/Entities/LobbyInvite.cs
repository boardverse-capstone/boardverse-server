using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Lời mời tham gia phòng chờ (lobby) gửi từ host hoặc thành viên cho người khác.
/// Áp dụng cho cả public/private lobby (private lobby chỉ có thể join qua invite).
/// BR-LOBBY-INVITE-01: Một (LobbyId, InviteeId) chỉ có 1 Pending record tại 1 thời điểm.
/// BR-LOBBY-INVITE-02: Inviter phải là thành viên active của lobby.
/// BR-LOBBY-INVITE-03: Invitee không được là thành viên active của lobby.
/// BR-LOBBY-INVITE-04: Invitee không bị lobby host block (BR-FRIEND-02).
/// </summary>
public class LobbyInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LobbyId { get; set; }

    /// <summary>Người gửi lời mời (host hoặc thành viên active của lobby).</summary>
    public Guid InviterId { get; set; }

    /// <summary>Người nhận lời mời.</summary>
    public Guid InviteeId { get; set; }

    public LobbyInviteStatus Status { get; set; } = LobbyInviteStatus.Pending;

    /// <summary>Thời điểm hết hạn (mặc định 24h sau khi gửi).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Thời điểm người nhận phản hồi (accept/decline).</summary>
    public DateTime? RespondedAt { get; set; }

    /// <summary>Lời nhắn kèm theo lời mời (vd: "Catan 4 người nhé, mình đợi 19h tại quán ABC").</summary>
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual Lobby Lobby { get; set; } = null!;
    public virtual User Inviter { get; set; } = null!;
    public virtual User Invitee { get; set; } = null!;
}