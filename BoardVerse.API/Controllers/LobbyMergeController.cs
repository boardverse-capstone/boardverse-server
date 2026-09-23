using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// API xử lý ghép nhóm lobby (Lobby Merge).
/// Theo Exception Path §4 — boardverse-business-context.mdc.
///
/// Kịch bản: Nhóm A gồm A1, A2, A3, A4 đang chơi. A1, A2 về sớm.
/// A3 muốn chuyển sang Nhóm B (đang active tại quán).
/// Staff quét mã A3 → POST /merge-requests → duyệt → A3 được ghép vào Nhóm B.
///
/// Role: Staff/Manager của quán đang vận hành.
/// Demo mode: HTTP header X-Bypass-Demo-Locks=true hoặc query ?bypassDemoLocks=true
/// bỏ qua BR-USER-LIMIT-02/03 khi duyệt merge.
/// </summary>
[ApiController]
[Route("api/cafes/{cafeId:guid}/lobby-merge")]
[Authorize(Roles = "Manager,CafeStaff")]
public class LobbyMergeController : BaseApiController
{
    private readonly ILobbyMergeService _mergeService;
    private readonly ICafeRepository _cafeRepository;

    public LobbyMergeController(
        ILobbyMergeService mergeService,
        ICafeRepository cafeRepository)
    {
        _mergeService = mergeService;
        _cafeRepository = cafeRepository;
    }

    private async Task ValidateStaffAccessAsync(Guid cafeId, CancellationToken ct)
    {
        var staffUserId = GetUserIdFromClaims();
        var isAllowed = await _cafeRepository.IsManagerOrStaffAsync(cafeId, staffUserId, ct);
        if (!isAllowed)
            throw new ForbiddenException(ApiErrorMessages.Pos.AccessForbidden(cafeId));
    }

    /// <summary>
    /// Tạo yêu cầu ghép nhóm.
    /// Staff POS gọi khi scan mã member A3 muốn nhập vào Nhóm B đang active.
    /// [Role: Manager/CafeStaff — phải thuộc quán đang vận hành.]
    /// Demo mode: bypass via X-Bypass-Demo-Locks header hoặc ?bypassDemoLocks=true.
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành.</param>
    /// <param name="request">Thông tin yêu cầu ghép nhóm.</param>
    /// <response code="201">Yêu cầu ghép nhóm đã được tạo.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc không hợp lệ.</response>
    /// <response code="403">Không có quyền vận hành quán này.</response>
    /// <response code="404">Quán hoặc lobby không tồn tại.</response>
    /// <response code="409">Lobby đích không active / đã có yêu cầu pending.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("merge-requests")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateMergeRequest(
        Guid cafeId,
        [FromBody] CreateLobbyMergeRequestDto request,
        CancellationToken ct)
    {
        await ValidateStaffAccessAsync(cafeId, ct);
        var staffUserId = GetUserIdFromClaims();

        var result = await _mergeService.CreateMergeRequestAsync(cafeId, staffUserId, request, ct);
        return this.NewResponse(201, "Tạo yêu cầu ghép nhóm thành công!", result);
    }

    /// <summary>
    /// Lấy chi tiết một yêu cầu ghép nhóm theo mã.
    /// [Role: Manager/CafeStaff — phải thuộc quán đang vận hành.]
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành.</param>
    /// <param name="requestId">Mã yêu cầu ghép nhóm.</param>
    /// <response code="200">Chi tiết yêu cầu ghép nhóm.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="404">Không tìm thấy yêu cầu.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("merge-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMergeRequest(
        Guid cafeId,
        Guid requestId,
        CancellationToken ct)
    {
        await ValidateStaffAccessAsync(cafeId, ct);

        var result = await _mergeService.GetMergeRequestAsync(requestId, ct);
        if (result == null)
            return this.NewResponse(404,
                BoardVerse.Core.Messages.ApiErrorMessages.Lobby.LobbyMerge.MergeRequestNotFound(requestId), null);

        return this.NewResponse(200, "OK", result);
    }

    /// <summary>
    /// Lấy danh sách tất cả yêu cầu ghép nhóm đang chờ (status = Pending) của một quán.
    /// [Role: Manager/CafeStaff — phải thuộc quán đang vận hành.]
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành.</param>
    /// <response code="200">Danh sách yêu cầu đang chờ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("merge-requests/pending")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPendingMergeRequests(Guid cafeId, CancellationToken ct)
    {
        await ValidateStaffAccessAsync(cafeId, ct);

        var result = await _mergeService.GetPendingRequestsAsync(cafeId, ct);
        return this.NewResponse(200, "OK", result);
    }

    /// <summary>
    /// Duyệt yêu cầu ghép nhóm — thực hiện ghép member vào lobby đích.
    /// Staff/Manager duyệt yêu cầu đã tạo. Thực hiện atomic trong transaction.
    /// BR-REQUIRED §17.4: FOR UPDATE lock trên ActiveSession để tránh race.
    /// BR-USER-LIMIT-02/03: Kiểm tra lịch chồng lấn và cap deposit của member.
    /// Demo mode: bypass BR-USER-LIMIT-02/03 qua X-Bypass-Demo-Locks header.
    /// [Role: Manager/CafeStaff — phải thuộc quán đang vận hành.]
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành.</param>
    /// <param name="requestId">Mã yêu cầu ghép nhóm.</param>
    /// <param name="dto">Ghi chú của staff khi duyệt (optional).</param>
    /// <response code="200">Ghép nhóm thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền vận hành quán này.</response>
    /// <response code="404">Yêu cầu không tồn tại / phiên chơi đích không tìm thấy.</response>
    /// <response code="409">Yêu cầu không còn Pending / đã hết hạn / lobby đích không active / không đủ ghế / lịch chồng lấn / cap deposit vượt / deposit đã captured.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("merge-requests/{requestId:guid}/approve")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ApproveMerge(
        Guid cafeId,
        Guid requestId,
        [FromBody] ReviewLobbyMergeRequestDto? dto,
        CancellationToken ct)
    {
        await ValidateStaffAccessAsync(cafeId, ct);
        var staffUserId = GetUserIdFromClaims();

        var result = await _mergeService.ApproveMergeAsync(
            cafeId, staffUserId, requestId, dto?.ReviewNote, ct);
        return this.NewResponse(200, "Ghép nhóm thành công!", result);
    }

    /// <summary>
    /// Từ chối yêu cầu ghép nhóm.
    /// Staff/Manager từ chối duyệt. Không thay đổi lobby hay session.
    /// [Role: Manager/CafeStaff — phải thuộc quán đang vận hành.]
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành.</param>
    /// <param name="requestId">Mã yêu cầu ghép nhóm.</param>
    /// <param name="dto">Ghi chú của staff khi từ chối (optional).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Đã từ chối yêu cầu ghép nhóm.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="404">Yêu cầu không tồn tại.</response>
    /// <response code="409">Yêu cầu không còn ở trạng thái Pending.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("merge-requests/{requestId:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RejectMerge(
        Guid cafeId,
        Guid requestId,
        [FromBody] ReviewLobbyMergeRequestDto? dto,
        CancellationToken ct)
    {
        await ValidateStaffAccessAsync(cafeId, ct);
        var staffUserId = GetUserIdFromClaims();

        var result = await _mergeService.RejectMergeAsync(
            cafeId, staffUserId, requestId,
            dto ?? new ReviewLobbyMergeRequestDto(), ct);
        return this.NewResponse(200, "Đã từ chối yêu cầu ghép nhóm.", result);
    }

    /// <summary>
    /// Lấy lịch sử ghép nhóm (audit log) của một lobby.
    /// Bao gồm các action: MergeRequested, MergeApproved, MergeRejected,
    /// MemberTransferred, SourceLobbyDissolved.
    /// [Role: Manager/CafeStaff — phải thuộc quán đang vận hành.]
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành.</param>
    /// <param name="lobbyId">Mã lobby cần xem lịch sử.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Danh sách audit log ghép nhóm.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("lobbies/{lobbyId:guid}/merge-history")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMergeHistory(
        Guid cafeId,
        Guid lobbyId,
        CancellationToken ct)
    {
        await ValidateStaffAccessAsync(cafeId, ct);

        var result = await _mergeService.GetLobbyMergeHistoryAsync(lobbyId, ct);
        return this.NewResponse(200, "OK", result);
    }

    /// <summary>
    /// Lấy danh sách tất cả yêu cầu ghép nhóm của một lobby (bất kể trạng thái).
    /// Bao gồm Pending, Approved, Rejected, Expired, Cancelled.
    /// [Role: Manager/CafeStaff — phải thuộc quán đang vận hành.]
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành.</param>
    /// <param name="lobbyId">Mã lobby cần xem yêu cầu ghép.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Danh sách yêu cầu ghép nhóm của lobby.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("lobbies/{lobbyId:guid}/merge-requests")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetLobbyMergeRequests(
        Guid cafeId,
        Guid lobbyId,
        CancellationToken ct)
    {
        await ValidateStaffAccessAsync(cafeId, ct);

        var result = await _mergeService.GetRequestsByLobbyAsync(lobbyId, ct);
        return this.NewResponse(200, "OK", result);
    }

    /// <summary>
    /// Hủy yêu cầu ghép nhóm (bởi người tạo hoặc staff).
    /// Chỉ hủy được khi status = Pending.
    /// [Role: Manager/CafeStaff — phải thuộc quán đang vận hành.]
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành.</param>
    /// <param name="requestId">Mã yêu cầu ghép nhóm.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Đã hủy yêu cầu ghép nhóm.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="404">Yêu cầu không tồn tại.</response>
    /// <response code="409">Yêu cầu không còn ở trạng thái Pending.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpDelete("merge-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CancelMergeRequest(
        Guid cafeId,
        Guid requestId,
        CancellationToken ct)
    {
        await ValidateStaffAccessAsync(cafeId, ct);
        var staffUserId = GetUserIdFromClaims();

        var result = await _mergeService.CancelMergeRequestAsync(cafeId, staffUserId, requestId, ct);
        return this.NewResponse(200, "Đã hủy yêu cầu ghép nhóm.", result);
    }
}
