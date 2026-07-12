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

    public PaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("booking-deposit")]
    public async Task<IActionResult> CreateBookingDepositPayment([FromBody] CreatePaymentRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _paymentService.CreateDepositPaymentAsync(request, userId);
        return this.NewResponse(200, "Tạo link thanh toán thành công.", result);
    }

    [HttpPost("session-payment")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> CreateSessionPayment([FromBody] CreateSessionPaymentRequestDto request)
    {
        var result = await _paymentService.CreateSessionPaymentAsync(request);
        return this.NewResponse(200, "Tạo thanh toán phiên chơi thành công.", result);
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
}
