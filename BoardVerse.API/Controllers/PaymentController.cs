using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BoardVerse.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentController : BaseApiController
{
    private readonly IPaymentService _paymentService;
    private readonly IManualPaymentService _manualPaymentService;

    public PaymentController(IPaymentService paymentService, IManualPaymentService manualPaymentService)
    {
        _paymentService = paymentService;
        _manualPaymentService = manualPaymentService;
    }

    [HttpPost("booking-deposit")]
    public async Task<IActionResult> CreateBookingDepositPayment([FromBody] CreatePaymentRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _paymentService.CreateDepositPaymentAsync(request, userId);
        return this.NewResponse(200, "Tạo link thanh toán thành công.", result);
    }

    /// <summary>
    /// Tạo lại QR thanh toán cho đơn cọc đang PENDING.
    /// QR cũ sẽ bị đánh dấu là EXPIRED.
    /// </summary>
    /// <param name="depositId">ID của đơn cọc</param>
    [HttpPost("booking-deposit/{depositId:guid}/regenerate-qr")]
    public async Task<IActionResult> RegenerateDepositQr(Guid depositId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _paymentService.RegenerateDepositQrAsync(depositId, userId);
        return this.NewResponse(200, "Tạo lại QR thanh toán thành công.", result);
    }

    [HttpPost("session-payment")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> CreateSessionPayment([FromBody] CreateSessionPaymentRequestDto request)
    {
        var result = await _paymentService.CreateSessionPaymentAsync(request);
        return this.NewResponse(200, "Tạo thanh toán phiên chơi thành công.", result);
    }

    /// <summary>
    /// Tạo lại QR thanh toán cho phiên chơi đang UNPAID.
    /// </summary>
    /// <param name="sessionId">ID của phiên chơi</param>
    [HttpPost("session-payment/{sessionId:guid}/regenerate-qr")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> RegenerateSessionQr(Guid sessionId)
    {
        var result = await _paymentService.RegenerateSessionQrAsync(sessionId);
        return this.NewResponse(200, "Tạo lại QR thanh toán phiên chơi thành công.", result);
    }

    /// <summary>
    /// Hoàn cọc đặt chỗ (BR-18: Hoàn 100% khi hủy do bất khả kháng từ phía quán).
    /// [Role: Manager, Admin]
    /// </summary>
    /// <param name="request">Thông tin hoàn cọc.</param>
    /// <response code="200">Hoàn cọc thành công.</response>
    /// <response code="400">Đơn cọc không ở trạng thái Paid.</response>
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
            DepositId = result.Id,
            Status = result.Status.ToString(),
            Amount = result.Amount,
            ProcessedAt = result.RefundedAt ?? DateTime.UtcNow
        });
    }

    /// <summary>
    /// Staff xác nhận thanh toán thủ công khi QR không hoạt động.
    /// Dùng cho trường hợp SePay và VietQR đều không khả dụng.
    /// BR-18: Hoàn cọc/sự cố vận hành - xử lý tiền mặt.
    /// </summary>
    /// <param name="request">Thông tin thanh toán thủ công.</param>
    /// <response code="200">Xác nhận thành công.</response>
    /// <response code="400">Thông tin không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền.</response>
    /// <response code="404">Không tìm thấy đơn.</response>
    [HttpPost("manual-confirm")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> ManualConfirmPayment([FromBody] ManualPaymentConfirmRequestDto request)
    {
        var staffId = GetUserIdFromClaims();
        var result = await _manualPaymentService.ConfirmManualPaymentAsync(request, staffId);
        return this.NewResponse(200, "Xác nhận thanh toán thủ công thành công.", result);
    }
}
