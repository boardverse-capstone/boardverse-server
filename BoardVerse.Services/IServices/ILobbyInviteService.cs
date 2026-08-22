using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.DTOs.LobbyInvite;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface ILobbyInviteService
{
    /// <summary>
    /// Gửi lời mời cho inviteeId. Inviter phải là thành viên active của lobby.
    /// Cả public/private lobby đều cho phép gửi invite.
    /// </summary>
    Task<LobbyInviteResponseDto> SendInviteAsync(Guid lobbyId, Guid inviterId, SendLobbyInviteRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accept invite. Chỉ invitee mới có thể accept.
    /// Sau khi accept, tự động join lobby (gọi ILobbyService.JoinLobbyAsync).
    /// </summary>
    Task<LobbyInviteResponseDto> AcceptInviteAsync(Guid inviteId, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decline invite. Invitee từ chối.
    /// </summary>
    Task<LobbyInviteResponseDto> DeclineInviteAsync(Guid inviteId, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inviter hủy lời mời đã gửi.
    /// </summary>
    Task CancelInviteAsync(Guid inviteId, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lời mời đang Pending của current user (inbox).
    /// </summary>
    Task<IReadOnlyList<LobbyInviteResponseDto>> GetMyPendingInvitesAsync(Guid inviteeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tất cả lời mời của current user (filter optional theo status).
    /// </summary>
    Task<IReadOnlyList<LobbyInviteResponseDto>> GetMyInvitesAsync(Guid inviteeId, string? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy share info (lobbyId + shareCode + isPrivate) để client hiển thị copy button.
    /// Chỉ thành viên của lobby mới xem được share code.
    /// </summary>
    Task<LobbyShareInfoDto> GetShareInfoAsync(Guid lobbyId, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách bạn bè (Friendship.Accepted) của current user kèm trạng thái
    /// quan hệ với lobby cụ thể để client render danh sách mời vào lobby.
    /// <para>Mỗi friend được gắn <see cref="LobbyInvitableFriendDto.InviteStatus"/>:</para>
    /// <list type="bullet">
    ///   <item><description><see cref="LobbyInviteFriendStatus.Invitable"/> — có thể mời.</description></item>
    ///   <item><description><see cref="LobbyInviteFriendStatus.InvitePending"/> — đã gửi invite Pending.</description></item>
    ///   <item><description><see cref="LobbyInviteFriendStatus.AlreadyMember"/> — friend đã ở trong lobby.</description></item>
    ///   <item><description><see cref="LobbyInviteFriendStatus.BlockedByThem"/> / <see cref="LobbyInviteFriendStatus.BlockedByMe"/> — bị block.</description></item>
    ///   <item><description><see cref="LobbyInviteFriendStatus.LobbyClosed"/> — lobby đã đóng.</description></item>
    /// </list>
    /// <para>Chỉ thành viên active của lobby mới gọi được endpoint này.</para>
    /// </summary>
    /// <param name="lobbyId">Mã lobby.</param>
    /// <param name="currentUserId">User hiện tại (host/member muốn mời).</param>
    /// <param name="query">Filter (search, onlineOnly, minKarma, status, limit).</param>
    Task<IReadOnlyList<LobbyInvitableFriendDto>> GetInvitableFriendsForLobbyAsync(
        Guid lobbyId,
        Guid currentUserId,
        LobbyInvitableFriendsQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy lịch sử invite của lobby — host/member xem những ai đã được mời,
    /// ai đã accept/decline, pending bao nhiêu, v.v.
    /// <para>Chỉ thành viên active của lobby mới gọi được.</para>
    /// </summary>
    /// <param name="lobbyId">Mã lobby.</param>
    /// <param name="currentUserId">User hiện tại.</param>
    /// <param name="status">Filter optional theo LobbyInviteStatus (Pending / Accepted / Declined / Expired / Cancelled).</param>
    /// <param name="limit">Giới hạn (1-200, mặc định 100).</param>
    Task<IReadOnlyList<LobbyInviteResponseDto>> GetLobbyInvitesAsync(
        Guid lobbyId,
        Guid currentUserId,
        string? status = null,
        int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi lại 1 invite đã ở trạng thái terminal (Declined / Expired / Cancelled).
    /// Reset về Pending + gia hạn ExpiresAt + tạo record mới (giữ lịch sử invite cũ).
    /// <para>Chỉ inviter cũ hoặc host mới gửi lại được.</para>
    /// </summary>
    /// <param name="inviteId">Mã invite cần gửi lại.</param>
    /// <param name="currentUserId">User hiện tại.</param>
    /// <returns>Invite mới (Pending) với ID khác invite cũ.</returns>
    Task<LobbyInviteResponseDto> ResendInviteAsync(Guid inviteId, Guid currentUserId);
}