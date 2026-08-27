using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Payments;
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

    // SePay HMAC headers — value giữ nguyên case-insensitive.
    private const string HeaderSignature = "X-SePay-Signature";
    private const string HeaderTimestamp = "X-SePay-Timestamp";
    private const string AuthorizationHeader = "Authorization";

    public SePayWebhookController(
        IPaymentService paymentService,
        ILogger<SePayWebhookController> logger,
        IHostEnvironment env)
    {
        _paymentService = paymentService;
        _logger = logger;
        _env = env;
    }

    /// <summary>
/// Nhận webhook từ SePay, verify signature và xử lý thanh toán. [Role: Public — webhook endpoint không auth.]
/// </summary>
/// <param name="cancellationToken">Token để cancel request khi SePay retry hoặc client disconnect.</param>
/// <response code="200">Webhook đã verify + xử lý thành công.</response>
/// <response code="400">Payload JSON rỗng hoặc không parse được.</response>
/// <response code="401">Signature verification thất bại (Bad HMAC, expired timestamp, missing API key).</response>
/// <response code="500">Lỗi xử lý payment nội bộ (DB exception, gateway response lỗi).</response>
[HttpPost]
    public async Task<IActionResult> ReceiveWebhook(CancellationToken cancellationToken = default)
    {
        // BẮT BUỘC đọc raw body TRƯỚC khi ASP.NET parse JSON.
        // Request.EnableBuffering() cho phép đọc lại body nhiều lần.
        Request.EnableBuffering();
        var rawBody = await new StreamReader(Request.Body, leaveOpen: true)
            .ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        // Parse JSON từ rawBody (giữ cùng DTO cũ để handler downstream không đổi).
        SePayWebhookDto? webhook;
        try
        {
            webhook = System.Text.Json.JsonSerializer.Deserialize<SePayWebhookDto>(
                rawBody,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (webhook == null)
            {
                _logger.LogWarning("SePay webhook payload empty or invalid JSON.");
                return BadRequest(new { status = "error", message = "Invalid payload." });
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "SePay webhook JSON parse failed.");
            return BadRequest(new { status = "error", message = "Invalid JSON." });
        }

        // === Webhook signature verification (3 mode: None / ApiKey / HmacSha256) ===
        var signature = ExtractSignatureFromHeaders(Request.Headers);
        var timestamp = Request.Headers[HeaderTimestamp].ToString();

        var verificationRequest = new SePayWebhookVerificationRequest(
            Signature: signature,
            Timestamp: timestamp,
            RawBody: rawBody);

        var (isValid, errorMessage) = await _paymentService.VerifyWebhookRequestAsync(
            verificationRequest, cancellationToken);

        if (!isValid)
        {
            _logger.LogWarning(
                "SePay webhook signature verification failed. OrderId={OrderId}, Error={Error}",
                webhook.OrderId, errorMessage);
            return Unauthorized(new { status = "error", message = errorMessage });
        }

        // Derive legacy fields (OrderId/Status/Amount/GatewayTransactionId) từ BankAPINotify payload.
        webhook.Normalize();

        try
        {
            await _paymentService.HandleSePayWebhookAsync(webhook, cancellationToken);
            return Ok(new { status = "ok" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SePay webhook processing failed.");
            return StatusCode(500, new { status = "error", message = ApiErrorMessages.Payment.SePayWebhookProcessingFailed });
        }
    }

    /// <summary>
    /// Extract signature theo mode SePay đang dùng:
    /// - HMAC-SHA256: header <c>X-SePay-Signature</c> (VD: <c>sha256=abc...</c>).
    /// - API Key: header <c>Authorization</c> với format <c>Apikey &lt;token&gt;</c>.
    /// Caller đã pre-select mode qua SePayAccount.WebhookAuthType.
    /// </summary>
    private static string? ExtractSignatureFromHeaders(IHeaderDictionary headers)
    {
        // Ưu tiên X-SePay-Signature (HMAC mode).
        var sigHeader = headers[HeaderSignature].ToString();
        if (!string.IsNullOrWhiteSpace(sigHeader))
        {
            return sigHeader.Trim();
        }

        // Fallback: Authorization header (ApiKey mode).
        var authHeader = headers[AuthorizationHeader].ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            const string apiKeyPrefix = "Apikey ";
            if (authHeader.StartsWith(apiKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return authHeader.Substring(apiKeyPrefix.Length).Trim();
            }

            // Một số client gửi raw token không có prefix.
            return authHeader.Trim();
        }

        return null;
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
    public async Task<IActionResult> MockWebhook([FromBody] MockWebhookRequestDto request, CancellationToken cancellationToken = default)
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

            await _paymentService.HandleSePayWebhookAsync(webhook, cancellationToken);
            return Ok(new { status = "ok" });
        }
catch (Exception ex)
{
_logger.LogError(ex, "Mock webhook processing failed.");
return StatusCode(500, new { status = "error", message = ApiErrorMessages.Payment.SePayMockWebhookProcessingFailed });
}
    }
}
