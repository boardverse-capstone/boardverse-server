using BoardVerse.Core.DTOs.Wallet;
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

    public AdminWalletController(IWalletService walletService)
    {
        _walletService = walletService;
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
}
