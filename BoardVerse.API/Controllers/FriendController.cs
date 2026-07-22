using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

[ApiController]
[Route("api/v1/friends")]
[Authorize]
public class FriendController : BaseApiController
{
    private readonly IFriendService _friendService;
    private readonly IFriendNoteService _friendNoteService;
    private readonly IFriendReportService _friendReportService;

    public FriendController(
        IFriendService friendService,
        IFriendNoteService friendNoteService,
        IFriendReportService friendReportService)
    {
        _friendService = friendService;
        _friendNoteService = friendNoteService;
        _friendReportService = friendReportService;
    }

    /// <summary>
    /// Gửi lời mời kết bạn cho một user. [Role: Player]
    /// </summary>
    /// <param name="request">Mã người nhận + lời nhắn (≤ 200 ký tự, optional).</param>
    /// <response code="201">Đã gửi lời mời.</response>
    /// <response code="400">Không thể gửi cho chính mình / tài khoản không hoạt động.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Bị block / privacy không cho phép.</response>
    /// <response code="404">Không tìm thấy người nhận.</response>
    /// <response code="409">Đã là bạn bè / đã có lời mời pending.</response>
    /// <response code="429">Vượt quá giới hạn gửi request/giờ.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("requests")]
    public async Task<IActionResult> SendFriendRequest([FromBody] SendFriendRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.SendFriendRequestAsync(userId, request);
        return this.NewResponse(201, ApiSuccessMessages.Friend.RequestSent, result);
    }

    /// <summary>
    /// Accept lời mời kết bạn. [Role: Player — chỉ addressee]
    /// </summary>
    /// <param name="id">Mã friendship.</param>
    /// <response code="200">Đã chấp nhận.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải người nhận / bị block / friend limit.</response>
    /// <response code="404">Không tìm thấy lời mời.</response>
    /// <response code="409">Lời mời không ở trạng thái chờ.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("requests/{id:guid}/accept")]
    public async Task<IActionResult> AcceptFriendRequest(Guid id)
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.AcceptFriendRequestAsync(userId, id);
        return this.NewResponse(200, ApiSuccessMessages.Friend.RequestAccepted, result);
    }

    /// <summary>
    /// Từ chối lời mời kết bạn. [Role: Player — chỉ addressee]
    /// </summary>
    /// <param name="id">Mã friendship.</param>
    /// <response code="200">Đã từ chối.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải người nhận.</response>
    /// <response code="404">Không tìm thấy.</response>
    /// <response code="409">Không ở trạng thái Pending.</response>
    [HttpPost("requests/{id:guid}/decline")]
    public async Task<IActionResult> DeclineFriendRequest(Guid id)
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.DeclineFriendRequestAsync(userId, id);
        return this.NewResponse(200, ApiSuccessMessages.Friend.RequestDeclined, result);
    }

    /// <summary>
    /// Đánh dấu đã đọc lời mời (inbox notification). [Role: Player — chỉ addressee]
    /// </summary>
    /// <param name="id">Mã friendship.</param>
    /// <response code="200">Đã đánh dấu đã đọc.</response>
    [HttpPost("requests/{id:guid}/read")]
    public async Task<IActionResult> MarkRequestAsRead(Guid id)
    {
        var userId = GetUserIdFromClaims();
        await _friendService.MarkRequestAsReadAsync(userId, id);
        return this.NewResponse(200, "Đã đánh dấu lời mời là đã đọc.", data: null);
    }

    /// <summary>
    /// Hủy kết bạn. [Role: Player — một trong hai bên]. Auto-cancel lobby invite Pending.
    /// </summary>
    /// <param name="id">Mã friendship.</param>
    /// <response code="200">Đã xóa quan hệ bạn bè.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền xóa.</response>
    /// <response code="404">Không tìm thấy.</response>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RemoveFriend(Guid id)
    {
        var userId = GetUserIdFromClaims();
        await _friendService.RemoveFriendshipAsync(userId, id);
        return this.NewResponse(200, ApiSuccessMessages.Friend.Removed, data: null);
    }

    /// <summary>
    /// Chặn user. [Role: Player]
    /// </summary>
    /// <param name="targetUserId">Mã người bị chặn.</param>
    /// <response code="200">Đã chặn.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không thể chặn Admin / chính mình.</response>
    /// <response code="404">Không tìm thấy người bị chặn.</response>
    [HttpPost("block/{targetUserId:guid}")]
    public async Task<IActionResult> BlockUser(Guid targetUserId)
    {
        var userId = GetUserIdFromClaims();
        await _friendService.BlockUserAsync(userId, targetUserId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.Blocked, data: null);
    }

    /// <summary>
    /// Bỏ chặn user. [Role: Player — chỉ người đã chặn]
    /// </summary>
    /// <param name="targetUserId">Mã người bị chặn.</param>
    /// <response code="200">Đã bỏ chặn.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Bạn không phải người đã chặn.</response>
    /// <response code="404">Không có quan hệ chặn.</response>
    [HttpDelete("block/{targetUserId:guid}")]
    public async Task<IActionResult> UnblockUser(Guid targetUserId)
    {
        var userId = GetUserIdFromClaims();
        await _friendService.UnblockUserAsync(userId, targetUserId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.Unblocked, data: null);
    }

    /// <summary>
    /// Danh sách bạn bè (status = Accepted). [Role: Player]
    /// </summary>
    /// <response code="200">Danh sách bạn bè kèm activity status.</response>
    /// <response code="401">Thiếu token.</response>
    [HttpGet]
    public async Task<IActionResult> GetFriends()
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.GetFriendsAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.ListRetrieved, result);
    }

    /// <summary>
    /// Danh sách bạn bè kèm trạng thái hoạt động (online / recently active / away / offline). [Role: Player]
    /// </summary>
    [HttpGet("activity")]
    public async Task<IActionResult> GetFriendsActivity()
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.GetFriendsActivityAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.ActivityRetrieved, result);
    }

    /// <summary>
    /// Lời mời kết bạn đang chờ mà current user NHẬN ĐƯỢC (inbox). [Role: Player]
    /// </summary>
    [HttpGet("requests/received")]
    public async Task<IActionResult> GetPendingReceivedRequests()
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.GetPendingReceivedRequestsAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.PendingRequestsRetrieved, result);
    }

    /// <summary>
    /// Lời mời kết bạn đã gửi đi nhưng chưa phản hồi (sent). [Role: Player]
    /// </summary>
    [HttpGet("requests/sent")]
    public async Task<IActionResult> GetPendingSentRequests()
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.GetPendingSentRequestsAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.PendingRequestsRetrieved, result);
    }

    /// <summary>
    /// Tìm user theo username để gửi lời mời kết bạn. Kết quả kèm FriendshipStatus + MutualFriendsCount. [Role: Player]
    /// </summary>
    /// <param name="q">Từ khóa (≥ 2 ký tự).</param>
    /// <param name="limit">Giới hạn kết quả (1-50, mặc định 20).</param>
    /// <response code="200">Danh sách user kèm trạng thái quan hệ.</response>
    /// <response code="400">Từ khóa quá ngắn.</response>
    /// <response code="401">Thiếu token.</response>
    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string q, [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return BadRequest("Từ khóa tìm kiếm phải có ít nhất 2 ký tự.");
        }

        var userId = GetUserIdFromClaims();
        var result = await _friendService.SearchUsersAsync(userId, q, limit);
        return this.NewResponse(200, ApiSuccessMessages.Friend.SearchCompleted, result);
    }

    /// <summary>
    /// Gợi ý kết bạn: friends-of-friends + người cùng chơi lobby gần đây. [Role: Player]
    /// </summary>
    /// <param name="limit">Giới hạn kết quả (1-50, mặc định 20).</param>
    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions([FromQuery] int limit = 20)
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.GetFriendSuggestionsAsync(userId, limit);
        return this.NewResponse(200, ApiSuccessMessages.Friend.SuggestionsRetrieved, result);
    }

    /// <summary>
    /// Lấy danh sách bạn chung giữa current user và otherUser. [Role: Player]
    /// </summary>
    /// <param name="otherUserId">Mã user khác.</param>
    [HttpGet("{otherUserId:guid}/mutual")]
    public async Task<IActionResult> GetMutualFriends(Guid otherUserId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.GetMutualFriendsAsync(userId, otherUserId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.MutualFriendsRetrieved, result);
    }

    /// <summary>
    /// Xem friend list của user khác (tôn trọng IsFriendListPublic). [Role: Player]
    /// </summary>
    /// <param name="otherUserId">Mã user khác.</param>
    [HttpGet("{otherUserId:guid}/list")]
    public async Task<IActionResult> GetOtherUserFriends(Guid otherUserId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendService.GetOtherUserFriendsAsync(userId, otherUserId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.ListRetrieved, result);
    }

    /// <summary>
    /// Cập nhật quyền riêng tư cho friend list. [Role: Player]
    /// </summary>
    /// <param name="dto">IsFriendListPublic / AcceptFriendRequestsFrom / FriendLimit.</param>
    [HttpPut("privacy")]
    public async Task<IActionResult> UpdatePrivacy([FromBody] UpdateFriendPrivacyDto dto)
    {
        var userId = GetUserIdFromClaims();
        await _friendService.UpdatePrivacyAsync(userId, dto);
        return this.NewResponse(200, ApiSuccessMessages.Friend.PrivacyUpdated, data: null);
    }

    // ===== Friend Notes =====

    /// <summary>
    /// Lấy tất cả ghi chú của current user. [Role: Player]
    /// </summary>
    [HttpGet("notes")]
    public async Task<IActionResult> GetNotes()
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendNoteService.GetMyNotesAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.NotesRetrieved, result);
    }

    /// <summary>
    /// Tạo/cập nhật ghi chú cho một friend. [Role: Player]
    /// </summary>
    /// <param name="friendUserId">Mã friend.</param>
    /// <param name="dto">Alias (bắt buộc), Note + Tags (optional).</param>
    [HttpPut("notes/{friendUserId:guid}")]
    public async Task<IActionResult> UpsertNote(Guid friendUserId, [FromBody] UpsertFriendNoteDto dto)
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendNoteService.UpsertNoteAsync(userId, friendUserId, dto);
        return this.NewResponse(200, ApiSuccessMessages.Friend.NoteUpdated, result);
    }

    /// <summary>
    /// Xóa ghi chú. [Role: Player — chỉ chủ sở hữu]
    /// </summary>
    /// <param name="noteId">Mã ghi chú.</param>
    [HttpDelete("notes/{noteId:guid}")]
    public async Task<IActionResult> DeleteNote(Guid noteId)
    {
        var userId = GetUserIdFromClaims();
        await _friendNoteService.DeleteNoteAsync(userId, noteId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.NoteDeleted, data: null);
    }

    // ===== Friend Reports =====

    /// <summary>
    /// Báo cáo vi phạm một user. Chỉ báo cáo được người đang là bạn bè (BR-FRIEND-REPORT-01). [Role: Player]
    /// </summary>
    /// <param name="dto">TargetUserId + Category + Reason (5-1000 ký tự).</param>
    [HttpPost("reports")]
    public async Task<IActionResult> SubmitReport([FromBody] CreateFriendReportDto dto)
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendReportService.SubmitReportAsync(userId, dto);
        return this.NewResponse(201, ApiSuccessMessages.Friend.ReportSubmitted, result);
    }

    /// <summary>
    /// Lấy danh sách báo cáo của current user. [Role: Player]
    /// </summary>
    [HttpGet("reports")]
    public async Task<IActionResult> GetMyReports()
    {
        var userId = GetUserIdFromClaims();
        var result = await _friendReportService.GetMyReportsAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.Friend.ListRetrieved, result);
    }
}
