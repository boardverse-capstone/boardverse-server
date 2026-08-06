using BoardVerse.Core.DTOs.LobbyInvite;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

[ApiController]
[Route("api/v1/lobbies")]
[Authorize]
public class LobbyInviteController : BaseApiController
{
    private readonly ILobbyInviteService _inviteService;
    private readonly ILobbyService _lobbyService;

    public LobbyInviteController(ILobbyInviteService inviteService, ILobbyService lobbyService)
    {
        _inviteService = inviteService;
        _lobbyService = lobbyService;
    }

    /// <summary>
    /// Gửi lời mời tham gia lobby cho một user. [Role: Player — chỉ thành viên active của lobby]
    /// Áp dụng cho cả public/private lobby. Private lobby chỉ join được qua invite hoặc share code.
    /// </summary>
    /// <param name="lobbyId">Mã phòng chờ.</param>
    /// <param name="request">Mã người được mời + lời nhắn (optional).</param>
    /// <response code="201">Đã gửi lời mời.</response>
    /// <response code="400">Mời chính mình.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải thành viên lobby.</response>
    /// <response code="404">Không tìm thấy lobby.</response>
    /// <response code="409">Người được mời đã là thành viên / đã có pending invite / lobby đã đóng.</response>
    [HttpPost("{lobbyId:guid}/invites")]
    public async Task<IActionResult> SendInvite(Guid lobbyId, [FromBody] SendLobbyInviteRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _inviteService.SendInviteAsync(lobbyId, userId, request);
        return this.NewResponse(201, ApiSuccessMessages.LobbyInvite.InviteSent, result);
    }

    /// <summary>
    /// Accept lời mời. Tự động join lobby sau khi accept. [Role: Player — chỉ invitee]
    /// </summary>
    /// <param name="inviteId">Mã lời mời.</param>
    /// <response code="200">Đã accept và join lobby.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải người nhận.</response>
    /// <response code="404">Không tìm thấy lời mời.</response>
    /// <response code="409">Lobby đã đóng/đầy hoặc lời mời không còn Pending.</response>
    [HttpPost("invites/{inviteId:guid}/accept")]
    public async Task<IActionResult> AcceptInvite(Guid inviteId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _inviteService.AcceptInviteAsync(inviteId, userId);
        return this.NewResponse(200, ApiSuccessMessages.LobbyInvite.InviteAccepted, result);
    }

    /// <summary>
    /// Từ chối lời mời. [Role: Player — chỉ invitee]
    /// </summary>
    /// <param name="inviteId">Mã lời mời.</param>
    /// <response code="200">Đã từ chối.</response>
    [HttpPost("invites/{inviteId:guid}/decline")]
    public async Task<IActionResult> DeclineInvite(Guid inviteId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _inviteService.DeclineInviteAsync(inviteId, userId);
        return this.NewResponse(200, ApiSuccessMessages.LobbyInvite.InviteDeclined, result);
    }

    /// <summary>
    /// Inviter hủy lời mời đã gửi. [Role: Player — chỉ inviter]
    /// </summary>
    /// <param name="inviteId">Mã lời mời.</param>
    /// <response code="204">Đã hủy lời mời.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải inviter.</response>
    /// <response code="404">Không tìm thấy lời mời.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete("invites/{inviteId:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> CancelInvite(Guid inviteId)
    {
        var userId = GetUserIdFromClaims();
        await _inviteService.CancelInviteAsync(inviteId, userId);
        return NoContent();
    }

    /// <summary>
    /// Inbox: lời mời lobby đang Pending cho current user. [Role: Player]
    /// </summary>
    /// <response code="200">Danh sách lời mời đang chờ.</response>
    [HttpGet("invites/me/pending")]
    public async Task<IActionResult> GetMyPendingInvites()
    {
        var userId = GetUserIdFromClaims();
        var result = await _inviteService.GetMyPendingInvitesAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.LobbyInvite.InvitesRetrieved, result);
    }

    /// <summary>
    /// Tất cả lời mời lobby của current user (filter optional). [Role: Player]
    /// </summary>
    /// <param name="status">Pending/Accepted/Declined/Expired/Cancelled (optional).</param>
    /// <response code="200">Danh sách lời mời.</response>
    [HttpGet("invites/me")]
    public async Task<IActionResult> GetMyInvites([FromQuery] string? status)
    {
        var userId = GetUserIdFromClaims();
        var result = await _inviteService.GetMyInvitesAsync(userId, status);
        return this.NewResponse(200, ApiSuccessMessages.LobbyInvite.InvitesRetrieved, result);
    }

    /// <summary>
    /// Lấy lobby ID + share code để hiển thị nút copy &amp; share. [Role: Player — chỉ thành viên]
    /// </summary>
    /// <param name="lobbyId">Mã phòng chờ.</param>
    /// <response code="200">Trả về lobbyId + shareCode + isPrivate + status.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải thành viên.</response>
    /// <response code="404">Không tìm thấy lobby.</response>
    [HttpGet("{lobbyId:guid}/share-info")]
    public async Task<IActionResult> GetShareInfo(Guid lobbyId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _inviteService.GetShareInfoAsync(lobbyId, userId);
        return this.NewResponse(200, ApiSuccessMessages.LobbyInvite.ShareInfoRetrieved, result);
    }

    /// <summary>
    /// Join lobby bằng share code (6 ký tự). [Role: Player]
    /// Dùng cho cả public/private. Private lobby chỉ join được qua share code hoặc invite.
    /// </summary>
    /// <param name="request">Body chứa shareCode.</param>
    /// <response code="200">Đã join lobby.</response>
    /// <response code="400">Share code trống.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="404">Share code không hợp lệ.</response>
    /// <response code="409">Đã là thành viên / lobby đầy / lobby đã đóng.</response>
    [HttpPost("join-by-code")]
    public async Task<IActionResult> JoinByShareCode([FromBody] JoinLobbyByShareCodeRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _lobbyService.JoinLobbyByShareCodeAsync(request.ShareCode, userId);
        return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyJoined, result);
    }

    /// <summary>
    /// Lấy lịch sử invite của lobby (Pending/Accepted/Declined/Expired/Cancelled).
    /// [Role: Player — chỉ thành viên active của lobby]
    /// </summary>
    /// <param name="lobbyId">Mã phòng chờ.</param>
    /// <param name="status">Filter optional: Pending / Accepted / Declined / Expired / Cancelled.</param>
    /// <param name="limit">Giới hạn kết quả (1-200, mặc định 100).</param>
    /// <response code="200">Danh sách invite của lobby (sắp xếp theo CreatedAt desc).</response>
    /// <response code="400">Status filter không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải thành viên lobby.</response>
    /// <response code="404">Không tìm thấy lobby.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{lobbyId:guid}/invites")]
    [ProducesResponseType(typeof(IReadOnlyList<LobbyInviteResponseDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetLobbyInvites(
        Guid lobbyId,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100)
    {
        var userId = GetUserIdFromClaims();
        var result = await _inviteService.GetLobbyInvitesAsync(lobbyId, userId, status, limit);
        return this.NewResponse(200, ApiSuccessMessages.LobbyInvite.LobbyInvitesRetrieved, result);
    }

    /// <summary>
    /// Gửi lại 1 invite đã ở trạng thái terminal (Declined / Expired / Cancelled).
    /// Tạo record mới (Pending), reset ExpiresAt = now + 24h.
    /// [Role: Player — chỉ inviter cũ hoặc host lobby]
    /// </summary>
    /// <param name="inviteId">Mã invite cần gửi lại.</param>
    /// <response code="201">Đã gửi lại lời mời (trả về invite mới).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải inviter cũ / host.</response>
    /// <response code="404">Không tìm thấy invite / lobby.</response>
    /// <response code="409">Invite đã Accepted / đã có Pending mới / lobby đã đóng / invitee đã là member / vi phạm rate limit.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("invites/{inviteId:guid}/resend")]
    [ProducesResponseType(typeof(LobbyInviteResponseDto), 201)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> ResendInvite(Guid inviteId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _inviteService.ResendInviteAsync(inviteId, userId);
        return this.NewResponse(201, ApiSuccessMessages.LobbyInvite.InviteResent, result);
    }

    /// <summary>
    /// Lấy danh sách bạn bè (Friendship.Accepted) của current user kèm trạng thái
    /// mời vào lobby để render UI danh sách mời (nút "Mời" / "Đã gửi" / "Đã trong phòng" / v.v.).
    /// [Role: Player — chỉ thành viên active của lobby]
    /// </summary>
    /// <param name="lobbyId">Mã phòng chờ.</param>
    /// <param name="search">Từ khóa tìm username (case-insensitive contains).</param>
    /// <param name="onlineOnly">Chỉ lấy friend đang Online / RecentlyActive.</param>
    /// <param name="minKarma">Karma tối thiểu (lọc bạn dưới ngưỡng).</param>
    /// <param name="status">Filter theo nhiều status (comma-separated, vd: <c>Invitable,InvitePending</c>).</param>
    /// <param name="limit">Giới hạn kết quả (1-200, mặc định 100).</param>
    /// <response code="200">Danh sách bạn bè kèm trạng thái invite.</response>
    /// <response code="400">Status filter không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải thành viên lobby.</response>
    /// <response code="404">Không tìm thấy lobby.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{lobbyId:guid}/invitable-friends")]
    [ProducesResponseType(typeof(IReadOnlyList<LobbyInvitableFriendDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetInvitableFriends(
        Guid lobbyId,
        [FromQuery] string? search = null,
        [FromQuery] bool onlineOnly = false,
        [FromQuery] int? minKarma = null,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100)
    {
        var userId = GetUserIdFromClaims();
        var query = new LobbyInvitableFriendsQuery
        {
            Search = search,
            OnlineOnly = onlineOnly,
            MinKarma = minKarma,
            Status = status,
            Limit = limit
        };
        var result = await _inviteService.GetInvitableFriendsForLobbyAsync(lobbyId, userId, query);
        return this.NewResponse(200, ApiSuccessMessages.LobbyInvite.InvitableFriendsRetrieved, result);
    }
}