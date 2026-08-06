using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Controller cho Ví BVC + sổ cái ledger.
/// Theo BR § II + § III — boardverse-lobby-booking-deposit-bvc.mdc.
/// Phase 1: top-up (tiền thật → BVC) + đọc balance + lịch sử.
/// Phase 3: thêm <c>POST /api/v1/reservations/confirm</c> sẽ dùng
/// ledger + wallet cho deposit hold/capture/release/forfeit.
/// </summary>
[ApiController]
[Route("api/v1/wallet")]
[Authorize]
[Produces("application/json")]
[Tags("Wallet")]
public class WalletController : BaseApiController
{
    private readonly IWalletService _walletService;

    public WalletController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    /// <summary>
    /// Lấy số dư ví BVC của player đang đăng nhập.
    /// Auto-create ví rỗng nếu user lần đầu truy cập.
    /// [Role: Player — chỉ chính chủ; Admin có thể truy cập <c>/admin/wallet/{userId}</c> riêng.]
    /// </summary>
    /// <param name="includeHeld">
    /// Có trả <c>HeldBalance</c> không. Mặc định <c>false</c> (an toàn UI mobile).
    /// Đặt <c>true</c> cho màn hình Wallet chi tiết.
    /// </param>
    /// <response code="200">Trả về ví của user (auto-create nếu chưa có).</response>
    /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet]
    [ProducesResponseType(typeof(WalletDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetWallet([FromQuery] bool includeHeld = false)
    {
        var userId = GetUserIdFromClaims();
        var wallet = await _walletService.GetOrCreateWalletAsync(userId, includeHeld);
        return NewResponse(200, "Lấy thông tin ví BVC thành công.", wallet);
    }

    /// <summary>
    /// Tạo đơn top-up BVC từ tiền thật (VND) qua SePay master account.
    /// Validate BR § II.2: tối thiểu 10.000 VND (= 10 BVC), bội số 1.000 VND.
    /// BR § XVII.1: idempotency — gửi lại cùng <c>IdempotencyKey</c> trả về cùng kết quả.
    /// [Role: Player — đã đăng nhập, account không bị suspended/banned.]
    /// </summary>
    /// <param name="request">Số tiền VND + idempotency key.</param>
    /// <response code="201">Tạo đơn top-up thành công, trả về URL thanh toán.</response>
    /// <response code="400">Dữ liệu không hợp lệ (dưới min, không chia hết).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Tài khoản đang bị hạn chế.</response>
    /// <response code="500">Lỗi hệ thống hoặc SePay gateway.</response>
    [HttpPost("topup")]
    [ProducesResponseType(typeof(TopUpResponseDto), 201)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> CreateTopUp([FromBody] TopUpRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var response = await _walletService.CreateTopUpAsync(userId, request);
        return NewResponse(201, "Tạo đơn top-up BVC thành công.", response);
    }

    /// <summary>
    /// Lịch sử sổ cái BVC của player đang đăng nhập (read-only).
    /// Sắp xếp mới nhất trước. Phân trang.
    /// [Role: Player — chỉ chính chủ.]
    /// </summary>
    /// <param name="page">Số trang, bắt đầu từ 1. Mặc định 1.</param>
    /// <param name="pageSize">Số entry mỗi trang. Mặc định 20, tối đa 100.</param>
    /// <response code="200">Trả về trang lịch sử ledger.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(BvcTransactionPageDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserIdFromClaims();
        var response = await _walletService.GetTransactionsAsync(userId, page, pageSize);
        return NewResponse(200, "Lấy lịch sử giao dịch BVC thành công.", response);
    }

    /// <summary>
    /// Hủy đơn top-up BVC đang Pending (chưa thanh toán).
    /// Set <c>Status = Cancelled</c>; webhook SePay tới sau sẽ bị reject tự động.
    /// Chỉ chính chủ đơn mới hủy được. Không thể hủy đơn đã Paid/Expired/Failed/Cancelled.
    /// [Role: Player — chỉ chính chủ.]
    /// </summary>
    /// <param name="topUpId">Id của <c>BvcTopUpRequest</c>.</param>
    /// <response code="200">Hủy đơn thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ đơn.</response>
    /// <response code="404">Không tìm thấy đơn top-up.</response>
    /// <response code="409">Đơn không ở trạng thái Pending (đã Paid/Expired/Failed/Cancelled).</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpDelete("topup/{topUpId:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> CancelTopUp([FromRoute] Guid topUpId)
    {
        var userId = GetUserIdFromClaims();
        await _walletService.CancelTopUpAsync(topUpId, userId);
        return NewResponse(200, "Hủy đơn top-up BVC thành công.", null);
    }

    /// <summary>
    /// Đổi số tiền đơn top-up BVC đang Pending (chưa thanh toán).
    /// Đơn cũ được set <c>Cancelled</c>; đơn mới được tạo với SePay PaymentUrl + OrderId mới.
    /// Validate cùng rule với <c>POST /wallet/topup</c> (min 10.000 VND, bội số 1.000 VND).
    /// Chỉ chính chủ đơn mới đổi được. Không thể đổi đơn đã terminal.
    /// [Role: Player — chỉ chính chủ.]
    /// </summary>
    /// <param name="topUpId">Id của <c>BvcTopUpRequest</c>.</param>
    /// <param name="request">Số tiền VND mới + idempotency key mới.</param>
    /// <response code="200">Cập nhật thành công, trả về QR mới với số tiền mới.</response>
    /// <response code="400">Amount dưới min / không chia hết cho 1.000.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ đơn.</response>
    /// <response code="404">Không tìm thấy đơn top-up.</response>
    /// <response code="409">Đơn không ở Pending, hoặc idempotency key đã dùng.</response>
    /// <response code="500">Lỗi hệ thống hoặc SePay gateway.</response>
    [HttpPatch("topup/{topUpId:guid}")]
    [ProducesResponseType(typeof(TopUpResponseDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(object), 403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> UpdateTopUpAmount(
        [FromRoute] Guid topUpId,
        [FromBody] UpdateTopUpRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var response = await _walletService.UpdateTopUpAmountAsync(topUpId, userId, request);
        return NewResponse(200, "Cập nhật số tiền top-up BVC thành công.", response);
    }
}
