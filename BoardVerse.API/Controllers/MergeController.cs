using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// M1 / Option A — API xử lý refund PER-MEMBER deposit khi member merge sang lobby khác (Exception 4).
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §B3.1, §B4.
///
/// <para>
/// Kịch bản: Member A3 đang chơi tại Nhóm A muốn chuyển sang Nhóm B (đang active tại quán).
/// Staff POS gọi endpoint này → <see cref="IMergeService.HandleMemberMergeAsync"/> refund
/// per-member deposit (nếu có) về ví của member, đồng thời ghi audit log.
/// </para>
///
/// <para>
/// Role: Staff/Manager/Admin của quán đang vận hành.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/cafes/{cafeId:guid}/member-merge")]
[Authorize(Roles = "Manager,CafeStaff,Admin")]
public class MergeController : BaseApiController
{
    private readonly IMergeService _mergeService;
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly ICafeRepository _cafeRepository;

    public MergeController(
        IMergeService mergeService,
        IActiveSessionRepository activeSessionRepository,
        ICafeRepository cafeRepository)
    {
        _mergeService = mergeService;
        _activeSessionRepository = activeSessionRepository;
        _cafeRepository = cafeRepository;
    }

    /// <summary>
    /// Xử lý merge member sang lobby khác — refund per-member deposit (nếu có).
    /// [Role: Manager/CafeStaff của cafe đang vận hành; Admin]
    /// </summary>
    /// <param name="cafeId">Mã quán đang vận hành — staff phải thuộc quán này.</param>
    /// <param name="request">Member + Lobby nguồn + Lobby đích (optional) + idempotency key.</param>
    /// <response code="200">Merge đã xử lý (refund hoặc skip nếu Guest/no deposit).</response>
    /// <response code="400">Dữ liệu không hợp lệ (idempotency key rỗng, memberId không hợp lệ).</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc không hợp lệ.</response>
    /// <response code="403">Không có quyền vận hành quán này.</response>
    /// <response code="404">Member không tồn tại.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("handle")]
    public async Task<IActionResult> HandleMemberMerge(
        Guid cafeId,
        [FromBody] HandleMemberMergeRequestDto request,
        CancellationToken ct = default)
    {
        if (request == null)
        {
            return NewResponse(400,
                ApiErrorMessages.System.ReservationInvalidRequest, null);
        }

        if (request.MemberId == Guid.Empty)
        {
            return NewResponse(400, "MemberId không được rỗng.", null);
        }

        if (request.FromLobbyId == Guid.Empty)
        {
            return NewResponse(400, "FromLobbyId không được rỗng.", null);
        }

        // ===== Permission check: staff/manager phải thuộc cafe =====
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin)
        {
            var staffUserId = GetUserIdFromClaims();
            var isAllowed = await _cafeRepository.IsManagerOrStaffAsync(cafeId, staffUserId, ct);
            if (!isAllowed)
            {
                throw new ForbiddenException(
                    ApiErrorMessages.Payment.ManualConfirmNotAuthorizedForCafe(cafeId));
            }
        }

        var actorUserId = GetUserIdFromClaims();

        var response = await _mergeService.HandleMemberMergeAsync(
            memberId: request.MemberId,
            fromLobbyId: request.FromLobbyId,
            toLobbyId: request.ToLobbyId,
            staffUserId: actorUserId,
            ct);

        return NewResponse(200, "Xử lý merge member thành công.", response);
    }
}

/// <summary>
/// Request body cho <c>POST /api/v1/cafes/{cafeId}/member-merge/handle</c>.
/// M1 / Option A — docs/design/host-deposit-discount-and-bvc-payment-design.md §B3.1.
/// </summary>
public class HandleMemberMergeRequestDto
{
    /// <summary>ID của ActiveSessionMember cần merge.</summary>
    public Guid MemberId { get; set; }

    /// <summary>ID của Lobby mà member đang ở (lobby nguồn).</summary>
    public Guid FromLobbyId { get; set; }

    /// <summary>
    /// ID của Lobby đích. Null = member tách khỏi lobby cũ nhưng không vào lobby mới (stand-alone).
    /// </summary>
    public Guid? ToLobbyId { get; set; }
}