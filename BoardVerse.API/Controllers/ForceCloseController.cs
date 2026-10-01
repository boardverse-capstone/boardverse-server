using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// M2/C2.16 — Force-close session endpoint (Gap #33).
/// Manager force-close phiên chơi khi còn unpaid members, chọn cách xử lý:
/// MarkNoShow | MarkAsDebt | CompensationByHost.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16.
/// (2026-10-01)
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Roles = "Manager,Admin")]
public class ForceCloseController : BaseApiController
{
    private readonly IForceCloseService _forceCloseService;
    private readonly ICafeRepository _cafeRepository;
    private readonly IActiveSessionRepository _activeSessionRepository;

    public ForceCloseController(
        IForceCloseService forceCloseService,
        ICafeRepository cafeRepository,
        IActiveSessionRepository activeSessionRepository)
    {
        _forceCloseService = forceCloseService;
        _cafeRepository = cafeRepository;
        _activeSessionRepository = activeSessionRepository;
    }

    /// <summary>
    /// Force-close phiên chơi với unpaid members. [Role: Manager — chủ quán; Admin]
    /// <para>
    /// Manager cần là chủ quán (ManagerId) của session.CafeId, hoặc role Admin.
    /// </para>
    /// </summary>
    /// <param name="sessionId">Mã phiên chơi cần force-close.</param>
    /// <param name="request">Cách xử lý unpaid members + lý do + cho phép late payment.</param>
    /// <response code="200">Force-close thành công, trả về chi tiết members đã xử lý.</response>
    /// <response code="400">Request không hợp lệ (handling/format/length sai).</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager chủ quán (hoặc Admin).</response>
    /// <response code="404">Không tìm thấy phiên chơi.</response>
    /// <response code="409">Phiên không ở trạng thái Unpaid, không có unpaid members, host chưa paid (compensation), hoặc còn unpaid khi AllowLatePayment=false.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("sessions/{sessionId:guid}/force-close")]
    public async Task<IActionResult> ForceCloseSession(
        Guid sessionId,
        [FromBody] ForceCloseRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return NewResponse(400, ApiErrorMessages.System.ReservationInvalidRequest, null);
        }

        // ===== Permission check =====
        var actorUserId = GetUserIdFromClaims();
        var session = await _activeSessionRepository.GetByIdAsync(sessionId, HttpContext.RequestAborted)
            ?? throw new NotFoundException(ApiErrorMessages.Session.SessionNotFoundById(sessionId));

        if (!User.IsInRole("Admin"))
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(session.CafeId, HttpContext.RequestAborted);
            if (cafe == null || cafe.ManagerId != actorUserId)
            {
                throw new ForbiddenException(
                    ApiErrorMessages.Payment.ManualConfirmNotAuthorizedForCafe(session.CafeId));
            }
        }

        var result = await _forceCloseService.ForceCloseWithUnpaidAsync(
            sessionId, request, actorUserId, HttpContext.RequestAborted);

        return NewResponse(200, ApiSuccessMessages.Session.ForceCloseCompleted, result);
    }
}