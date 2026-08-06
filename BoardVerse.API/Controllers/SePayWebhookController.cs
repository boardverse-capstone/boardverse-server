using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace BoardVerse.API.Controllers;

[ApiController]
[Route("api/payments/sepay/webhook")]
public class SePayWebhookController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<SePayWebhookController> _logger;
    private readonly IHostEnvironment _env;

    public SePayWebhookController(
        IPaymentService paymentService,
        ILogger<SePayWebhookController> logger,
        IHostEnvironment env)
    {
        _paymentService = paymentService;
        _logger = logger;
        _env = env;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveWebhook([FromBody] SePayWebhookDto webhook)
    {
        try
        {
            // SePay BankAPINotify gửi payload thô (content/transferAmount/transferType/...).
            // Derive các field legacy (OrderId/Status/Amount/GatewayTransactionId)
            // ngay tại entry point để handler downstream không phải thay đổi.
            webhook.Normalize();

            await _paymentService.HandleSePayWebhookAsync(webhook);
            return Ok(new { status = "ok" });
        }
catch (Exception ex)
{
_logger.LogError(ex, "SePay webhook processing failed.");
return StatusCode(500, new { status = "error", message = ApiErrorMessages.Payment.SePayWebhookProcessingFailed });
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
return Ok(new { message = ApiErrorMessages.Payment.SePayReturnSuccess, orderId });
}
return BadRequest(new { message = ApiErrorMessages.Payment.SePayReturnFailed, orderId });
    }

    /// <summary>
    /// Mock webhook để test payment flow mà không cần SePay thật. [Dev/Test Only]
    /// P0 Fix #4: Gate with environment check to prevent production abuse.
    /// </summary>
    /// <param name="request">Thông tin mock payment.</param>
    /// <response code="200">Mock webhook xử lý thành công.</response>
    /// <response code="403">Mock endpoint chỉ khả dụng trong Development.</response>
    /// <response code="500">Lỗi xử lý.</response>
    [HttpPost("mock")]
    public async Task<IActionResult> MockWebhook([FromBody] MockWebhookRequestDto request)
    {
        // P0 Fix #4: Gate endpoint to development only
if (!_env.IsDevelopment())
{
_logger.LogWarning("Mock webhook called in non-development environment. Blocked.");
return StatusCode(403, new { status = "forbidden", message = ApiErrorMessages.Payment.SePayMockEndpointBlocked });
}

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
                Status = request.Status ?? "success",
                ReferenceCode = request.ReferenceCode,
                TransferAmount = request.Amount,
                TransferType = (request.Status ?? "success") == "success" ? "in" : "out",
                TransactionDate = request.Status == "success" ? DateTime.UtcNow : null
            };
            webhook.Normalize();

            await _paymentService.HandleSePayWebhookAsync(webhook);
            return Ok(new { status = "ok" });
        }
catch (Exception ex)
{
_logger.LogError(ex, "Mock webhook processing failed.");
return StatusCode(500, new { status = "error", message = ApiErrorMessages.Payment.SePayMockWebhookProcessingFailed });
}
    }
}
