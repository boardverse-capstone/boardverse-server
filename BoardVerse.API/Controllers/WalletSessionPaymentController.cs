using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// M2 / Case 2 — Member BVC bill payment endpoints.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.6, §C2.7, §C2.8.
/// </summary>
[ApiController]
[Route("api/v1/sessions/{sessionId:guid}")]
[Authorize]
public class WalletSessionPaymentController : BaseApiController
{
    private readonly IWalletSessionPaymentService _walletSessionPaymentService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<WalletSessionPaymentController> _logger;

    public WalletSessionPaymentController(
        IWalletSessionPaymentService walletSessionPaymentService,
        IFeatureFlagService featureFlagService,
        ILogger<WalletSessionPaymentController> logger)
    {
        _walletSessionPaymentService = walletSessionPaymentService;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// M2 / Task C2.6: Preview hóa đơn cá nhân của 1 member trong session.
    /// </summary>
    /// <param name="sessionId">ActiveSession.Id.</param>
    /// <param name="memberId">ActiveSessionMember.Id.</param>
    /// <response code="200">Preview thành công.</response>
    /// <response code="403">Player không phải member này và không phải host.</response>
    /// <response code="404">Session hoặc member không tồn tại.</response>
    [HttpGet("members/{memberId:guid}/bill-preview")]
    public async Task<IActionResult> GetMemberBillPreview(
        Guid sessionId,
        Guid memberId,
        CancellationToken ct = default)
    {
        // Feature flag check (M2 Phase 6)
        if (!_featureFlagService.IsMemberBvcPaymentEnabled())
        {
            _logger.LogInformation(
                "Member BVC payment feature disabled. SessionId={SessionId}, MemberId={MemberId}",
                sessionId, memberId);
            return this.NewResponse(403,
                "Feature thanh toán BVC đang tắt. Vui lòng liên hệ admin.", null);
        }

        var (actorUserId, actorRole) = GetViewerContext();

        var preview = await _walletSessionPaymentService.GetMemberBillPreviewAsync(
            sessionId, memberId, actorUserId, actorRole, ct);
        return this.NewResponse(200, "Lấy preview bill thành công.", preview);
    }

    /// <summary>
    /// M2 / Task C2.7: Member (hoặc staff) thanh toán bill cá nhân bằng BVC.
    /// </summary>
    /// <param name="sessionId">ActiveSession.Id.</param>
    /// <param name="memberId">ActiveSessionMember.Id.</param>
    /// <param name="request">BvcAmount + IdempotencyKey.</param>
    /// <response code="200">Thanh toán thành công.</response>
    /// <response code="400">BvcAmount không hợp lệ hoặc vượt totalDue.</response>
    /// <response code="403">Feature tắt hoặc không có quyền.</response>
    /// <response code="404">Session hoặc member không tồn tại.</response>
    /// <response code="409">Session không ở Unpaid; member đã paid; duplicate UserId; idempotency conflict.</response>
    [HttpPost("members/{memberId:guid}/pay-bill")]
    public async Task<IActionResult> PayMemberBill(
        Guid sessionId,
        Guid memberId,
        [FromBody] MemberBillPaymentRequestDto request,
        CancellationToken ct = default)
    {
        // Feature flag check (M2 Phase 6)
        if (!_featureFlagService.IsMemberBvcPaymentEnabled())
        {
            return this.NewResponse(403,
                "Feature thanh toán BVC đang tắt. Vui lòng liên hệ admin.", null);
        }

        if (request == null)
        {
            return this.NewResponse(400,
                ApiErrorMessages.System.ReservationInvalidRequest, null);
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return this.NewResponse(400,
                "Idempotency key là bắt buộc.", null);
        }

        if (request.BvcAmount <= 0)
        {
            return this.NewResponse(400,
                "BvcAmount phải lớn hơn 0.", null);
        }

        var (actorUserId, actorRole) = GetViewerContext();

        var result = await _walletSessionPaymentService.PayMemberBillAsync(
            sessionId, memberId, request, actorUserId, actorRole, ct);

        return this.NewResponse(200, "Thanh toán bill bằng BVC thành công.", result);
    }

    /// <summary>
    /// M2 / Task C2.8: Refund BVC bill đã thanh toán (Gap #11 bill sai / dispute).
    /// Staff/Manager/Admin only.
    /// </summary>
    /// <param name="sessionId">ActiveSession.Id (chỉ để routing — không dùng trong logic).</param>
    /// <param name="memberPaymentAuditLogId">ID của MemberPaymentAuditLog cần refund.</param>
    /// <param name="request">Reason + IdempotencyKey.</param>
    /// <response code="200">Refund thành công.</response>
    /// <response code="403">Không có quyền refund.</response>
    /// <response code="404">Audit log không tồn tại.</response>
    /// <response code="409">Audit log đã refund rồi.</response>
    [HttpPost("refund-bill/{memberPaymentAuditLogId:guid}")]
    [Authorize(Roles = "Admin,Manager,CafeStaff")]
    public async Task<IActionResult> RefundMemberBill(
        Guid sessionId,
        Guid memberPaymentAuditLogId,
        [FromBody] MemberBillRefundRequestDto request,
        CancellationToken ct = default)
    {
        if (request == null)
        {
            return this.NewResponse(400,
                ApiErrorMessages.System.ReservationInvalidRequest, null);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return this.NewResponse(400,
                "Lý do refund là bắt buộc.", null);
        }

        var (actorUserId, _) = GetViewerContext();

        var result = await _walletSessionPaymentService.RefundMemberBillAsync(
            memberPaymentAuditLogId, request, actorUserId, ct);

        return this.NewResponse(200, "Refund BVC bill thành công.", result);
    }

    /// <summary>
    /// Lấy (UserId, Role) từ JWT claims — copy pattern từ <see cref="CafePosController"/>.
    /// </summary>
    private (Guid UserId, string Role) GetViewerContext()
    {
        var userId = GetUserIdFromClaims();
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
        return (userId, role);
    }
}
