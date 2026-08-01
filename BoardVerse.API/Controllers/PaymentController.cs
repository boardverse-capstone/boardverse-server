using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Payment controller cho toàn bộ luồng thanh toán booking deposit và session payment.
/// Tất cả endpoint booking deposit đi qua đây (không có BookingController riêng).
/// BR-02, BR-03, BR-05, BR-06, BR-09, BR-15, BR-18.
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentController : BaseApiController
{
    private readonly IPaymentService _paymentService;
    private readonly IBookingDepositService _depositService;
    private readonly IManualPaymentService _manualPaymentService;

    public PaymentController(
        IPaymentService paymentService,
        IBookingDepositService depositService,
        IManualPaymentService manualPaymentService)
    {
        _paymentService = paymentService;
        _depositService = depositService;
        _manualPaymentService = manualPaymentService;
    }

    // ============================================================
    // DEPOSIT ENDPOINTS
    // ============================================================

    /// <summary>
    /// Lấy chi tiết đơn cọc theo ID. Dùng để mobile polling trạng thái sau khi tạo.
    /// [Role: Player — chỉ xem được đơn của mình (deposit.UserId == currentUserId);
    ///        Manager — chỉ xem được đơn thuộc quán của mình;
    ///        Admin — xem tất cả.]
    /// Theo mobile gap #6.
    /// </summary>
    /// <param name="depositId">Mã định danh đơn cọc.</param>
    /// <response code="200">Lấy chi tiết đơn cọc thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền xem đơn này.</response>
    /// <response code="404">Không tìm thấy đơn cọc.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("booking-deposit/{depositId:guid}")]
    [Authorize(Roles = "Manager,Admin,Player")]
    public async Task<IActionResult> GetDepositById(Guid depositId)
    {
        var deposit = await _depositService.GetByIdAsync(depositId)
            ?? throw new NotFoundException($"Không tìm thấy đơn cọc với ID: {depositId}");

        var userId = GetUserIdFromClaims();
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

        // P2 Fix #14 + mobile gap #6: AuthZ theo role.
        // Admin: xem tất cả.
        // Manager: chỉ đơn thuộc quán của mình (deposit.CafeManagerId == userId).
        // Player: chỉ đơn của chính mình (deposit.UserId == currentUserId).
        bool authorized = userRole switch
        {
            "Admin" => true,
            "Manager" => deposit.CafeManagerId == userId,
            "Player" => deposit.UserId == userId,
            _ => false
        };
        if (!authorized)
        {
            throw new ForbiddenException("Bạn không có quyền xem đơn cọc này.");
        }

        var response = BookingDepositResponseDto.FromEntity(deposit);
        return this.NewResponse(200, "Lấy chi tiết đơn cọc thành công.", response);
    }

    /// <summary>
    /// Lấy chi tiết đơn cọc theo mã đặt chỗ (OrderId / BookingCode).
    /// Dùng khi khách cung cấp mã đặt chỗ (trên app hoặc để debug).
    /// [Role: Player — chỉ đơn của mình; Manager — đơn thuộc quán của mình; Admin — xem tất cả.]
    /// Mobile gap #6.
    /// </summary>
    /// <param name="orderId">Mã đặt chỗ (OrderId).</param>
    /// <response code="200">Lấy chi tiết đơn cọc thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="404">Không tìm thấy đơn cọc.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("booking-deposit/by-order/{orderId}")]
    [Authorize(Roles = "Manager,Admin,Player")]
    public async Task<IActionResult> GetDepositByOrderId(string orderId)
    {
        var deposit = await _depositService.GetByOrderIdAsync(orderId.Trim())
            ?? throw new NotFoundException($"Không tìm thấy đơn cọc với mã đặt chỗ: {orderId}");

        var userId = GetUserIdFromClaims();
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        bool authorized = userRole switch
        {
            "Admin" => true,
            "Manager" => deposit.CafeManagerId == userId,
            "Player" => deposit.UserId == userId,
            _ => false
        };
        if (!authorized)
        {
            throw new ForbiddenException("Bạn không có quyền xem đơn cọc này.");
        }

        var response = BookingDepositResponseDto.FromEntity(deposit);
        return this.NewResponse(200, "Lấy chi tiết đơn cọc thành công.", response);
    }

    /// <summary>
    /// Tạo đơn cọc đặt chỗ và sinh QR thanh toán qua SePay.
    /// Áp dụng cho flow Player đặt cọc online (BR-05).
    /// - Validate BR-03: depositAmount &lt;= 50% giờ đầu của quán.
    /// - Sinh OrderId (BV-prefix) và TransferContent ngẫu nhiên.
    /// - Gọi SePay → VietQR tĩnh, QR không hết hạn.
    /// - BookingDeposit.Status = Pending.
    /// [Role: Player đã đăng nhập.]
    /// </summary>
    /// <param name="request">Thông tin tạo đơn cọc (depositId, amount).</param>
    /// <response code="200">Tạo link thanh toán thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ / vượt BR-03 / quán hết chỗ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ đơn cọc.</response>
    /// <response code="404">Không tìm thấy đơn cọc.</response>
    /// <response code="409">Đơn cọc đã được xử lý thanh toán trước đó.</response>
    /// <response code="500">Gateway lỗi không recover được.</response>
    [HttpPost("booking-deposit")]
    public async Task<IActionResult> CreateBookingDepositPayment([FromBody] CreatePaymentRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _paymentService.CreateDepositPaymentAsync(request, userId);
        return this.NewResponse(200, "Tạo link thanh toán thành công.", result);
    }

    /// <summary>
    /// Tạo lại QR thanh toán cho đơn cọc đang PENDING.
    /// QR cũ vẫn lưu lại trong DB để reference, không xóa.
    /// Không giới hạn số lần regenerate trong ngày.
    /// Sử dụng fallback chain: SePay primary → VietQR static.
    /// BR-06: Mỗi lần regenerate sinh TransferContent mới.
    /// [Role: Player — chỉ chủ đơn; Manager, Admin — tất cả.]
    /// </summary>
    /// <param name="depositId">Mã định danh đơn cọc đang PENDING.</param>
    /// <response code="200">Tạo lại QR thanh toán thành công.</response>
    /// <response code="400">Đơn cọc không ở trạng thái PENDING.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải chủ đơn cọc.</response>
    /// <response code="404">Không tìm thấy đơn cọc.</response>
    /// <response code="500">Gateway lỗi.</response>
    [HttpPost("booking-deposit/{depositId:guid}/regenerate-qr")]
    public async Task<IActionResult> RegenerateDepositQr(Guid depositId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _paymentService.RegenerateDepositQrAsync(depositId, userId);
        return this.NewResponse(200, "Tạo lại QR thanh toán thành công.", result);
    }

    /// <summary>
    /// Hoàn cọc đặt chỗ theo chính sách của quán.
    /// BR-18: Hoàn 100% khi quán hủy vì bất khả kháng (RefundPolicy = Full).
    /// BR-18: Hoàn theo elapsedHours khi khách hủy sớm (RefundPolicy = Partial):
    ///   - >= 24h → 50% hoàn; >= 12h → 25% hoàn; &lt; 12h → 0%.
    /// BR-18: Tịch thu toàn bộ khi khách không đến và RefundPolicy = None.
    /// [Role: Manager — chủ quán sở hữu đơn; Admin — tất cả.]
    /// </summary>
    /// <param name="request">Thông tin hoàn cọc (depositId, reason bắt buộc cho audit).</param>
    /// <response code="200">Hoàn cọc thành công. Trả về số tiền thực tế hoàn cho khách.</response>
    /// <response code="400">Đơn cọc không ở trạng thái Paid / thiếu lý do.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="404">Không tìm thấy đơn cọc.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("booking-deposit/refund")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> RefundDeposit([FromBody] RefundDepositRequestDto request)
    {
        var result = await _paymentService.RefundDepositAsync(request.DepositId, request.Reason);
        return this.NewResponse(200, "Hoàn cọc thành công.", new RefundDepositResponseDto
        {
            DepositId = result.Deposit.Id,
            Status = result.Deposit.Status.ToString(),
            Amount = result.Deposit.Amount,
            RefundedAmount = result.RefundedAmount,
            ProcessedAt = result.Deposit.RefundedAt ?? result.Deposit.ForfeitedAt ?? DateTime.UtcNow
        });
    }

    // ============================================================
    // SESSION PAYMENT ENDPOINTS
    // ============================================================

    /// <summary>
    /// Tạo QR thanh toán hóa đơn phiên chơi tại POS (sau khi kiểm kê linh kiện).
    /// BR-15: TotalAmount = Subtotal + PenaltyAmount - DepositAppliedAmount.
    /// Dùng VietQR tĩnh của từng cafe (bank info từ Cafe.SePayBankCode / SePayAccountNumber).
    /// [Role: Manager — chủ quán; CafeStaff — đã gắn quán.]
    /// </summary>
    /// <param name="request">Thông tin tạo thanh toán session.</param>
    /// <response code="200">Tạo thanh toán phiên chơi thành công.</response>
    /// <response code="400">Session không ở UNPAID / amount &lt;= 0 / cafe chưa cấu hình SePay.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Manager/CafeStaff của cafe.</response>
    /// <response code="404">Không tìm thấy session hoặc cafe.</response>
    /// <response code="500">Gateway lỗi.</response>
    [HttpPost("session-payment")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> CreateSessionPayment([FromBody] CreateSessionPaymentRequestDto request)
    {
        var result = await _paymentService.CreateSessionPaymentAsync(request);
        return this.NewResponse(200, "Tạo thanh toán phiên chơi thành công.", result);
    }

    /// <summary>
    /// Tạo lại QR thanh toán cho phiên chơi đang UNPAID.
    /// Sinh TransferContent mới mỗi lần regenerate.
    /// [Role: Manager, CafeStaff của cafe.]
    /// </summary>
    /// <param name="sessionId">Mã phiên chơi.</param>
    /// <response code="200">Tạo lại QR thành công.</response>
    /// <response code="400">Session không ở UNPAID.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Manager/CafeStaff của cafe.</response>
    /// <response code="404">Không tìm thấy session.</response>
    /// <response code="500">Gateway lỗi.</response>
    [HttpPost("session-payment/{sessionId:guid}/regenerate-qr")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> RegenerateSessionQr(Guid sessionId)
    {
        var result = await _paymentService.RegenerateSessionQrAsync(sessionId);
        return this.NewResponse(200, "Tạo lại QR thanh toán phiên chơi thành công.", result);
    }

    // ============================================================
    // MANUAL PAYMENT ENDPOINT
    // ============================================================

    /// <summary>
    /// Staff xác nhận thanh toán thủ công khi cả SePay và VietQR đều không khả dụng.
    /// Use case: Khách thanh toán tiền mặt trực tiếp cho POS; hoặc SePay + VietQR đều timeout.
    /// BR-18: Xử lý sự cố vận hành — phiếu thu tiền mặt thay vì QR.
    /// [Role: Manager — chủ quán; CafeStaff — đã gắn quán.]
    /// </summary>
    /// <param name="request">Thông tin thanh toán thủ công.</param>
    /// <response code="200">Xác nhận thành công.</response>
    /// <response code="400">Thông tin không hợp lệ / session không ở UNPAID.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Manager/CafeStaff của cafe.</response>
    /// <response code="404">Không tìm thấy session.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("manual-confirm")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> ManualConfirmPayment([FromBody] ManualPaymentConfirmRequestDto request)
    {
        var staffId = GetUserIdFromClaims();
        var result = await _manualPaymentService.ConfirmManualPaymentAsync(request, staffId);
        return this.NewResponse(200, "Xác nhận thanh toán thủ công thành công.", result);
    }
}
