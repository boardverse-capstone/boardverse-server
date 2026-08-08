using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin endpoints cho Ví BVC. Tách riêng <c>WalletController</c> để rõ ràng phân quyền.
/// Theo BR-RISK-05: mọi admin action ghi audit (PlayerActionHistory) + ledger entry.
/// Theo BR-RISK-07: chỉ Admin role.
/// </summary>
[ApiController]
[Route("api/v1/admin/wallet")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
[Tags("Admin - Wallet")]
public class AdminWalletController : BaseApiController
{
    private readonly IWalletService _walletService;
    private readonly IBvcRefundRequestService _refundRequestService;

    public AdminWalletController(
        IWalletService walletService,
        IBvcRefundRequestService refundRequestService)
    {
        _walletService = walletService;
        _refundRequestService = refundRequestService;
    }

    /// <summary>
    /// Lấy danh sách tất cả wallets (phân trang).
    /// Hỗ trợ filter theo search term, AccountStatus, RiskLevel.
    /// [Role: Admin]
    /// </summary>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Số item/trang (mặc định 20, max 100).</param>
    /// <param name="searchTerm">Tìm theo email, full name, hoặc userId.</param>
    /// <param name="statusFilter">Filter theo AccountStatus.</param>
    /// <param name="riskLevelFilter">Filter theo RiskLevel.</param>
    /// <response code="200">Danh sách wallets.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    [HttpGet]
    [ProducesResponseType(typeof(AdminWalletPageDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetAllWallets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] AccountStatus? statusFilter = null,
        [FromQuery] RiskLevel? riskLevelFilter = null)
    {
        var result = await _walletService.GetAllWalletsAsync(
            page, pageSize, searchTerm, statusFilter, riskLevelFilter);
        return NewResponse(200, "Danh sách wallets", result);
    }

    /// <summary>
    /// Lấy chi tiết wallet của một user (bao gồm thông tin user).
    /// [Role: Admin]
    /// </summary>
    /// <param name="userId">UserId cần xem.</param>
    /// <response code="200">Chi tiết wallet.</response>
    /// <response code="404">User không có ví BVC.</response>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(AdminWalletDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetWalletDetail(Guid userId)
    {
        var result = await _walletService.GetWalletDetailAsync(userId);
        if (result == null)
        {
            return NotFound(new { message = ApiErrorMessages.Wallet.NotFound(userId) });
        }
        return NewResponse(200, "Chi tiết wallet", result);
    }

    /// <summary>
    /// Lấy lịch sử giao dịch BVC của một user.
    /// [Role: Admin]
    /// </summary>
    /// <param name="userId">UserId cần xem lịch sử.</param>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Số item/trang (mặc định 20, max 100).</param>
    /// <response code="200">Lịch sử giao dịch.</response>
    [HttpGet("{userId:guid}/transactions")]
    [ProducesResponseType(typeof(AdminUserTransactionsPageDto), 200)]
    public async Task<IActionResult> GetUserTransactions(
        Guid userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _walletService.GetUserTransactionsAsync(userId, page, pageSize);
        return NewResponse(200, "Lịch sử giao dịch", result);
    }

    /// <summary>
    /// Thay đổi AccountStatus của user (lock/unlock/suspend/ban).
    /// Ghi PlayerActionHistory (BR-RISK-05).
    /// [Role: Admin]
    /// </summary>
    /// <param name="request">Thông tin thay đổi trạng thái.</param>
    /// <response code="200">Thay đổi thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="404">User không tìm thấy.</response>
    [HttpPost("set-status")]
    [ProducesResponseType(typeof(AdminSetStatusResultDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetAccountStatus([FromBody] AdminSetStatusRequestDto request)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _walletService.SetAccountStatusAsync(
            targetUserId: request.TargetUserId,
            newStatus: request.NewStatus,
            reason: request.Reason,
            expiresAt: request.ExpiresAt,
            adminUserId: adminUserId,
            idempotencyKey: request.IdempotencyKey);

        return NewResponse(200, $"Đã cập nhật trạng thái tài khoản của user '{request.TargetUserId}' thành '{request.NewStatus}'.", result);
    }

    /// <summary>
    /// Điều chỉnh số dư ví BVC của một user cụ thể (cộng hoặc trừ).
    /// Dùng cho: compensation (nhầm user), manual refund, support adjustment, fraud penalty.
    /// KHÔNG qua SePay — ghi thẳng ledger AdminCredit/AdminDebit + audit note.
    /// [Role: Admin — mọi action ghi PlayerActionHistory sau (TODO phase sau).]
    /// </summary>
    /// <param name="request">Target user, amount BVC, direction, lý do, idempotency key.</param>
    /// <response code="200">Điều chỉnh thành công, trả về snapshot số dư mới.</response>
    /// <response code="400">Dữ liệu không hợp lệ (amount ≤ 0, reason rỗng).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">User chưa có ví (sẽ auto-create ví rỗng rồi mới cộng/trừ).</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("adjust")]
    [ProducesResponseType(typeof(BvcHoldResult), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> AdjustBalance([FromBody] AdminAdjustBalanceRequestDto request)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _walletService.AdminAdjustBalanceAsync(
            targetUserId: request.TargetUserId,
            amountBvc: request.AmountBvc,
            isCredit: request.IsCredit,
            adminUserId: adminUserId,
            reason: request.Reason,
            idempotencyKey: request.IdempotencyKey);

        return NewResponse(200,
            $"Đã {(request.IsCredit ? "cộng" : "trừ")} {request.AmountBvc} BVC cho user '{request.TargetUserId}'.",
            result);
    }

    /// <summary>
    /// W-05: Verify SUM(ledger entries) = wallet.availableBalance.
    /// Logic: SUM(TopUp + AdminCredit) - SUM(DepositHold + AdminDebit + DepositCapture + DepositForfeit) = availableBalance.
    /// [Role: Admin]
    /// </summary>
    /// <param name="userId">UserId cần reconcile.</param>
    /// <response code="200">Kết quả reconcile.</response>
    /// <response code="404">User không có ví.</response>
    [HttpGet("{userId:guid}/reconcile")]
    [ProducesResponseType(typeof(WalletReconcileResultDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ReconcileWallet(Guid userId)
    {
        var result = await _walletService.ReconcileWalletAsync(userId);
        return NewResponse(200, "Kết quả reconcile ví BVC.", result);
    }

    // ============================================================
    // BVC Refund Request — admin xem/xét yêu cầu hoàn (BR-RISK-05).
    // ============================================================

    /// <summary>
    /// Admin xem danh sách yêu cầu hoàn BVC (phân trang + filter theo status, userId).
    /// [Role: Admin]
    /// </summary>
    /// <param name="statusFilter">Lọc theo status (Pending/Approved/Rejected/Cancelled).</param>
    /// <param name="userIdFilter">Lọc theo user gửi yêu cầu.</param>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Số item/trang (mặc định 20, max 100).</param>
    /// <response code="200">Danh sách refund request.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("refund-requests")]
    [ProducesResponseType(typeof(RefundRequestPageDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetRefundRequests(
        [FromQuery] RefundRequestStatus? statusFilter = null,
        [FromQuery] Guid? userIdFilter = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _refundRequestService.GetPagedAsync(
            statusFilter, userIdFilter, page, pageSize);
        return NewResponse(200, "Danh sách yêu cầu hoàn BVC.", result);
    }

    /// <summary>
    /// Admin xem chi tiết 1 yêu cầu hoàn BVC (kèm ledger entry context).
    /// [Role: Admin]
    /// </summary>
    /// <param name="requestId">Id của <c>BvcRefundRequest</c>.</param>
    /// <response code="200">Chi tiết refund request.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy yêu cầu.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("refund-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(RefundRequestResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetRefundRequestDetail([FromRoute] Guid requestId)
    {
        var result = await _refundRequestService.GetByIdAsync(requestId);
        if (result == null)
        {
            return NewResponse(404, ApiErrorMessages.Wallet.RefundRequestNotFound(requestId), null);
        }
        return NewResponse(200, "Chi tiết yêu cầu hoàn BVC.", result);
    }

    /// <summary>
    /// Admin duyệt hoặc từ chối yêu cầu hoàn BVC.
    /// Approve → tạo ledger AdminCredit + cộng ví + ghi PlayerActionHistory (BR-RISK-05).
    /// Reject → chỉ update status + ghi PlayerActionHistory.
    /// Idempotent theo <c>Idempotency-Key</c> header (BR § XVII.1).
    /// [Role: Admin]
    /// </summary>
    /// <param name="requestId">Id của <c>BvcRefundRequest</c>.</param>
    /// <param name="request">Decision (Approve/Reject), ApprovedAmountBvc (chỉ khi Approve), AdminNote.</param>
    /// <param name="idempotencyKey">Header Idempotency-Key, dùng để chống trùng resolve (BR § XVII.1).</param>
    /// <response code="200">Resolve thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (AdminNote &lt; 5 ký tự, ApprovedAmount ≤ 0).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy yêu cầu.</response>
    /// <response code="409">Yêu cầu đã được xử lý trước đó (không còn Pending).</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("refund-requests/{requestId:guid}/resolve")]
    [ProducesResponseType(typeof(RefundRequestResponseDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> ResolveRefundRequest(
        [FromRoute] Guid requestId,
        [FromBody] ResolveRefundRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return NewResponse(400, ApiErrorMessages.Wallet.IdempotencyKeyRequired, null);
        }

        var adminUserId = GetUserIdFromClaims();
        var result = await _refundRequestService.ResolveAsync(
            requestId, request, adminUserId, idempotencyKey);
        return NewResponse(200,
            $"Đã xử lý yêu cầu hoàn BVC '{requestId}' — kết quả: {result.Status}.",
            result);
    }
}
