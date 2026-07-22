using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.DTOs.LobbyInvite;

namespace BoardVerse.Services.IServices;

public interface ILobbyInviteService
{
    /// <summary>
    /// Gửi lời mời cho inviteeId. Inviter phải là thành viên active của lobby.
    /// Cả public/private lobby đều cho phép gửi invite.
    /// </summary>
    Task<LobbyInviteResponseDto> SendInviteAsync(Guid lobbyId, Guid inviterId, SendLobbyInviteRequestDto request);

    /// <summary>
    /// Accept invite. Chỉ invitee mới có thể accept.
    /// Sau khi accept, tự động join lobby (gọi ILobbyService.JoinLobbyAsync).
    /// </summary>
    Task<LobbyInviteResponseDto> AcceptInviteAsync(Guid inviteId, Guid currentUserId);

    /// <summary>
    /// Decline invite. Invitee từ chối.
    /// </summary>
    Task<LobbyInviteResponseDto> DeclineInviteAsync(Guid inviteId, Guid currentUserId);

    /// <summary>
    /// Inviter hủy lời mời đã gửi.
    /// </summary>
    Task CancelInviteAsync(Guid inviteId, Guid currentUserId);

    /// <summary>
    /// Lời mời đang Pending của current user (inbox).
    /// </summary>
    Task<IReadOnlyList<LobbyInviteResponseDto>> GetMyPendingInvitesAsync(Guid inviteeId);

    /// <summary>
    /// Tất cả lời mời của current user (filter optional theo status).
    /// </summary>
    Task<IReadOnlyList<LobbyInviteResponseDto>> GetMyInvitesAsync(Guid inviteeId, string? status);

    /// <summary>
    /// Lấy share info (lobbyId + shareCode + isPrivate) để client hiển thị copy button.
    /// Chỉ thành viên của lobby mới xem được share code.
    /// </summary>
    Task<LobbyShareInfoDto> GetShareInfoAsync(Guid lobbyId, Guid currentUserId);
}