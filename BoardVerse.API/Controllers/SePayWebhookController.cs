using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

[ApiController]
[Route("api/payments/sepay/webhook")]
public class SePayWebhookController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<SePayWebhookController> _logger;

    public SePayWebhookController(IPaymentService paymentService, ILogger<SePayWebhookController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveWebhook([FromBody] SePayWebhookDto webhook)
    {
        try
        {
            await _paymentService.HandleSePayWebhookAsync(webhook);
            return Ok(new { status = "ok" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SePay webhook processing failed.");
            return StatusCode(500, new { status = "error" });
        }
    }

    /// <summary>
    /// Redirect URL sau khi thanh toán SePay thành công.
    /// SePay sẽ redirect user về URL này.
    /// </summary>
    [HttpGet("return")]
    [AllowAnonymous]
    public IActionResult SePayReturn([FromQuery] string? orderId, [FromQuery] string? status)
    {
        if (status == "success")
        {
            return Ok(new { message = "Thanh toán thành công! Vui lòng quay lại ứng dụng.", orderId });
        }
        return BadRequest(new { message = "Thanh toán thất bại hoặc bị hủy.", orderId });
    }

    /// <summary>
    /// Mock webhook để test payment flow mà không cần SePay thật. [Role: Dev/Test]
    /// </summary>
    /// <param name="request">Thông tin mock payment.</param>
    /// <response code="200">Mock webhook xử lý thành công.</response>
    /// <response code="500">Lỗi xử lý.</response>
    [HttpPost("mock")]
    [AllowAnonymous]
    public async Task<IActionResult> MockWebhook([FromBody] MockWebhookRequestDto request)
    {
        try
        {
            var webhook = new SePayWebhookDto
            {
                Id = Guid.NewGuid().ToString(),
                Gateway = "SePay",
                GatewayTransactionId = $"TXN-MOCK-{Guid.NewGuid():N}",
                OrderId = request.OrderId,
                Amount = request.Amount,
                Currency = request.Currency ?? "VND",
                Status = request.Status,
                ReferenceCode = request.ReferenceCode,
                PaidAt = request.Status == "success" ? DateTime.UtcNow : null
            };

            await _paymentService.HandleSePayWebhookAsync(webhook);
            return Ok(new { status = "ok", webhook });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mock webhook processing failed.");
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }
}
