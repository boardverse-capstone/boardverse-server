using BoardVerse.API.Infrastructure;
using BoardVerse.Core.Data;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Settings;
using BoardVerse.Services.Services.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Debug/test endpoints cho payment.
/// Chỉ khả dụng khi ASPNETCORE_ENVIRONMENT=Development hoặc ENABLE_DEBUG=true.
/// </summary>
[ApiController]
[Route("api/debug/sepay")]
public class DebugSePayController : ControllerBase
{
    private readonly SePaySettings _sePaySettings;
    private readonly IVietQrClient _vietQrClient;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DebugSePayController> _logger;

    public DebugSePayController(
        IOptions<SePaySettings> sePaySettings,
        IVietQrClient vietQrClient,
        IWebHostEnvironment env,
        ILogger<DebugSePayController> logger)
    {
        _sePaySettings = sePaySettings.Value;
        _vietQrClient = vietQrClient;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Tạo VietQR thanh toán test.
    /// Trả về JSON với QR image URL và thông tin thanh toán.
    /// </summary>
    [HttpGet("checkout")]
    public IActionResult GetCheckout([FromQuery] string? orderId = null, [FromQuery] decimal? amount = null)
    {
        if (!IsDebugEnabled()) return NotFound();

        var orderIdValue = orderId ?? $"TEST-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var amountValue = amount ?? 100m;

        var qrUrl = _vietQrClient.GenerateQrUrl(
            _sePaySettings.BankCode,
            _sePaySettings.AccountNumber,
            amountValue,
            description: $"BoardVerse debug test - {orderIdValue}",
            accountHolder: _sePaySettings.AccountHolder);

        return Ok(new
        {
            orderId = orderIdValue,
            amount = amountValue,
            gateway = "VietQr",
            isSuccess = true,
            paymentUrl = qrUrl,
            qrImageUrl = qrUrl,
            requiresManualConfirmation = true,
            message = "Quét mã QR để thanh toán.",
            settings = new
            {
                environment = _sePaySettings.Environment,
                merchantId = _sePaySettings.MerchantId,
                bankCode = _sePaySettings.BankCode,
                accountNumber = _sePaySettings.AccountNumber,
                accountHolder = _sePaySettings.AccountHolder,
                webhookTokenSet = !string.IsNullOrWhiteSpace(_sePaySettings.WebhookToken)
            }
        });
    }

    /// <summary>
    /// Health check — kiểm tra config hiện tại.
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        if (!IsDebugEnabled()) return NotFound();

        return Ok(new
        {
            environment = _sePaySettings.Environment,
            merchantId = _sePaySettings.MerchantId,
            secretKeySet = !string.IsNullOrWhiteSpace(_sePaySettings.SecretKey),
            webhookTokenSet = !string.IsNullOrWhiteSpace(_sePaySettings.WebhookToken),
            apiBaseUrl = _sePaySettings.ApiBaseUrl,
            bankCode = _sePaySettings.BankCode,
            accountNumber = _sePaySettings.AccountNumber,
            accountHolder = _sePaySettings.AccountHolder,
            paymentMode = "VietQr_Static"
        });
    }

    /// <summary>
    /// Tạo đơn cọc test + immediately generate VietQR.
    /// Dùng cho debug/testing end-to-end payment flow.
    /// </summary>
    [HttpPost("test-deposit")]
    public async Task<IActionResult> CreateTestDeposit([FromQuery] decimal? amount = null)
    {
        if (!IsDebugEnabled()) return NotFound();

        var scope = HttpContext.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerse.Data.BoardVerseDbContext>();

        var cafe = await db.Cafes.FirstOrDefaultAsync(c => c.Id == DevSeedConstants.DemoCafeId);
        if (cafe == null)
            return BadRequest(new { error = $"Cafe {DevSeedConstants.DemoCafeId} not found. Run seeder first." });

        if (cafe.BasePrice == 0)
        {
            cafe.BasePrice = 100000m;
            await db.SaveChangesAsync();
            _logger.LogInformation("Auto-set DemoCafe BasePrice to 100000");
        }

        var depositId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var orderId = $"BV-D-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var depositAmount = amount ?? 100m;

        // Ensure QrUrl column is wide enough for VietQR URLs
        await db.Database.ExecuteSqlRawAsync($@"
            ALTER TABLE ""BookingDeposits"" ALTER COLUMN ""QrUrl"" TYPE varchar(2000)");

        await db.Database.ExecuteSqlRawAsync($@"
            DELETE FROM ""BookingDeposits""
            WHERE ""Id"" = '{depositId}' OR ""OrderId"" = '{orderId}'");

        var now = DateTime.UtcNow;
        var transferContent = $"BV-{depositId:N}";

        await db.Database.ExecuteSqlRawAsync($@"
            INSERT INTO ""BookingDeposits""
            (""Id"", ""ActiveSessionId"", ""Amount"", ""CafeId"", ""CafeManagerId"",
             ""CreatedAt"", ""ForfeitedAt"", ""MasterAccountId"", ""OrderId"", ""PaidAt"",
             ""RefundPolicy"", ""RefundedAt"", ""ReleasedAt"", ""ScheduledAt"",
             ""SePayTransactionId"", ""SePayTransferId"", ""Status"", ""TransferContent"", ""UpdatedAt"")
            VALUES
            ('{depositId}', NULL, {depositAmount},
             '{DevSeedConstants.DemoCafeId}', '{DevSeedConstants.ManagerUserId}',
             '{now:O}', NULL, NULL, '{orderId}', NULL,
             {(int)DepositRefundPolicy.Full}, NULL, NULL, NULL,
             NULL, NULL, {(int)BookingDepositStatus.Pending}, '{transferContent}', '{now:O}')");

        var qrUrl = _vietQrClient.GenerateQrUrl(
            _sePaySettings.BankCode,
            _sePaySettings.AccountNumber,
            depositAmount,
            description: transferContent,
            accountHolder: _sePaySettings.AccountHolder);

        await db.Database.ExecuteSqlRawAsync($@"
            UPDATE ""BookingDeposits""
            SET ""QrUrl"" = '{qrUrl.Replace("'", "''")}',
                ""QrExpiresAt"" = NULL::timestamp
            WHERE ""Id"" = '{depositId}'");

        _logger.LogInformation("Test deposit created: {DepositId}, QR={QrUrl}", depositId, qrUrl);

        return Ok(new
        {
            depositId,
            orderId,
            amount = depositAmount,
            cafeId = DevSeedConstants.DemoCafeId,
            cafeName = cafe.Name,
            basePrice = cafe.BasePrice,
            transferContent,
            status = BookingDepositStatus.Pending.ToString(),
            gateway = "VietQr",
            isSuccess = true,
            paymentUrl = qrUrl,
            qrImageUrl = qrUrl,
            requiresManualConfirmation = true,
            nextStep = "Quét QR để thanh toán, sau đó gọi POST /api/debug/sepay/mock-webhook"
        });
    }

    [HttpPost("mock-webhook")]
    public async Task<IActionResult> MockWebhook([FromBody] System.Text.Json.JsonElement body)
    {
        if (!IsDebugEnabled()) return NotFound();

        var scope = HttpContext.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerse.Data.BoardVerseDbContext>();

        var orderId = body.TryGetProperty("orderId", out var o) ? o.GetString() : null;
        var status = body.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (string.IsNullOrEmpty(orderId))
            return BadRequest(new { error = "orderId is required" });

        var deposit = await db.BookingDeposits.FirstOrDefaultAsync(d => d.OrderId == orderId);

        if (deposit != null && deposit.Status == BookingDepositStatus.Pending)
        {
            if (string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                deposit.Status = BookingDepositStatus.Refunded;
                deposit.RefundedAt = DateTime.UtcNow;
                deposit.UpdatedAt = DateTime.UtcNow;
                deposit.SePayTransactionId = $"MOCK-CANCEL-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                await db.SaveChangesAsync();
                _logger.LogInformation("Mock webhook: deposit {OrderId} marked CANCELLED", orderId);
                return Ok(new { status = "deposit_marked_cancelled", orderId });
            }

            deposit.Status = BookingDepositStatus.Paid;
            deposit.PaidAt = DateTime.UtcNow;
            deposit.SePayTransactionId = $"MOCK-TXN-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            deposit.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            _logger.LogInformation("Mock webhook: deposit {OrderId} marked PAID", orderId);
            return Ok(new { status = "deposit_marked_paid", orderId });
        }

        return Ok(new { status = "no_pending_deposit_found", orderId });
    }

    /// <summary>
    /// Debug HTML page để test: hiển thị QR, mock thanh toán, confirm webhook.
    /// </summary>
    [HttpGet("test-page")]
    public async Task<IActionResult> GetTestPage([FromQuery] decimal? amount = null)
    {
        if (!IsDebugEnabled()) return NotFound();

        var scope = HttpContext.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerse.Data.BoardVerseDbContext>();

        var cafe = await db.Cafes.FirstOrDefaultAsync(c => c.Id == DevSeedConstants.DemoCafeId);
        if (cafe == null)
            return BadRequest(new { error = $"Cafe {DevSeedConstants.DemoCafeId} not found." });

        if (cafe.BasePrice == 0)
        {
            cafe.BasePrice = 100000m;
            await db.SaveChangesAsync();
        }

        var depositId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var orderId = $"BV-D-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var depositAmount = amount ?? 100m;

        await db.Database.ExecuteSqlRawAsync($@"
            ALTER TABLE ""BookingDeposits"" ALTER COLUMN ""QrUrl"" TYPE varchar(2000)");

        await db.Database.ExecuteSqlRawAsync($@"
            DELETE FROM ""BookingDeposits""
            WHERE ""Id"" = '{depositId}' OR ""OrderId"" = '{orderId}'");

        var now = DateTime.UtcNow;
        var transferContent = $"BV-{depositId:N}";

        await db.Database.ExecuteSqlRawAsync($@"
            INSERT INTO ""BookingDeposits""
            (""Id"", ""ActiveSessionId"", ""Amount"", ""CafeId"", ""CafeManagerId"",
             ""CreatedAt"", ""ForfeitedAt"", ""MasterAccountId"", ""OrderId"", ""PaidAt"",
             ""RefundPolicy"", ""RefundedAt"", ""ReleasedAt"", ""ScheduledAt"",
             ""SePayTransactionId"", ""SePayTransferId"", ""Status"", ""TransferContent"", ""UpdatedAt"")
            VALUES
            ('{depositId}', NULL, {depositAmount},
             '{DevSeedConstants.DemoCafeId}', '{DevSeedConstants.ManagerUserId}',
             '{now:O}', NULL, NULL, '{orderId}', NULL,
             {(int)DepositRefundPolicy.Full}, NULL, NULL, NULL,
             NULL, NULL, {(int)BookingDepositStatus.Pending}, '{transferContent}', '{now:O}')");

        var qrUrl = _vietQrClient.GenerateQrUrl(
            _sePaySettings.BankCode,
            _sePaySettings.AccountNumber,
            depositAmount,
            description: transferContent,
            accountHolder: _sePaySettings.AccountHolder);

        await db.Database.ExecuteSqlRawAsync($@"
            UPDATE ""BookingDeposits""
            SET ""QrUrl"" = '{qrUrl.Replace("'", "''")}',
                ""QrExpiresAt"" = NULL::timestamp
            WHERE ""Id"" = '{depositId}'");

        var html = $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>BoardVerse - VietQR Debug Test</title>
<style>
  body {{ font-family: 'Segoe UI', sans-serif; max-width: 600px; margin: 40px auto; padding: 20px; background: #0f172a; color: #e2e8f0; }}
  .card {{ background: #1e293b; border-radius: 12px; padding: 24px; margin: 16px 0; border: 1px solid #334155; }}
  h1 {{ color: #38bdf8; margin-bottom: 4px; }}
  .subtitle {{ color: #64748b; font-size: 14px; margin-bottom: 24px; }}
  .qr-section {{ text-align: center; margin: 24px 0; }}
  .qr-section img {{ border-radius: 12px; border: 2px solid #334155; max-width: 250px; }}
  .badge {{ display: inline-block; padding: 4px 12px; border-radius: 20px; font-size: 12px; font-weight: 600; }}
  .badge-vietqr {{ background: #1e3a5f; color: #93c5fd; }}
  .info-grid {{ display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 16px; }}
  .info-item {{ background: #0f172a; border-radius: 8px; padding: 12px; }}
  .info-label {{ font-size: 11px; color: #64748b; text-transform: uppercase; letter-spacing: 0.05em; }}
  .info-value {{ font-size: 18px; font-weight: 600; color: #f1f5f9; margin-top: 4px; }}
  .info-value.small {{ font-size: 13px; font-family: monospace; }}
  .btn {{ display: inline-block; padding: 12px 24px; border-radius: 8px; font-size: 14px; font-weight: 600; cursor: pointer; border: none; text-align: center; margin: 4px; }}
  .btn-pay {{ background: #22c55e; color: #fff; }}
  .btn-pay:hover {{ background: #16a34a; }}
  .btn-cancel {{ background: #ef4444; color: #fff; }}
  .btn-cancel:hover {{ background: #dc2626; }}
  .btn-pay:disabled {{ background: #334155; color: #64748b; cursor: not-allowed; }}
  .status-bar {{ display: flex; align-items: center; gap: 8px; margin-bottom: 16px; }}
  .status-dot {{ width: 8px; height: 8px; border-radius: 50%; background: #64748b; }}
  .status-dot.pending {{ background: #f59e0b; }}
  .status-dot.paid {{ background: #22c55e; }}
  .note {{ font-size: 12px; color: #64748b; background: #0f172a; border-radius: 8px; padding: 12px; margin-top: 12px; }}
</style>
</head>
<body>
  <h1>BoardVerse VietQR</h1>
  <p class=""subtitle"">Deposit Payment Debug Test &mdash; {DateTime.Now:dd/MM/yyyy HH:mm}</p>

  <div class=""card"">
    <div class=""status-bar"">
      <div class=""status-dot pending"" id=""statusDot""></div>
      <strong id=""statusText"">Chờ thanh toán</strong>
      <span class=""badge badge-vietqr"">VietQR Static</span>
    </div>

    <div class=""info-grid"">
      <div class=""info-item"">
        <div class=""info-label"">Mã đơn</div>
        <div class=""info-value small"">{orderId}</div>
      </div>
      <div class=""info-item"">
        <div class=""info-label"">Số tiền</div>
        <div class=""info-value"">{depositAmount:N0} ₫</div>
      </div>
      <div class=""info-item"">
        <div class=""info-label"">Nội dung CK</div>
        <div class=""info-value small"">{transferContent}</div>
      </div>
      <div class=""info-item"">
        <div class=""info-label"">Quán</div>
        <div class=""info-value"" style=""font-size:14px"">{cafe.Name}</div>
      </div>
    </div>
  </div>

  <div class=""card"">
    <div class=""qr-section"">
      <img src=""{qrUrl}"" alt=""QR Code"" id=""qrImage"" onerror=""this.style.display='none';this.nextElementSibling.style.display='block';"">
      <div style=""display:none; color:#64748b; padding:40px;"">QR không tải được. Thử mở trực tiếp: <a href=""{qrUrl}"" target=""_blank"" style=""color:#38bdf8;"">mở QR</a></div>
    </div>
    <p style=""text-align:center; color:#64748b; font-size:13px;"">
      Nội dung: <strong style=""color:#f1f5f9;"">{transferContent}</strong><br>
      Số tiền: <strong style=""color:#f1f5f9;"">{depositAmount:N0} ₫</strong><br>
      Ngân hàng: <strong style=""color:#f1f5f9;"">{_sePaySettings.BankCode}</strong> - {(_sePaySettings.AccountNumber).Substring(0, 3)}****{(_sePaySettings.AccountNumber).Substring((_sePaySettings.AccountNumber).Length - 4)}
    </p>
  </div>

  <div class=""card"">
    <strong>Simulate Payment</strong>
    <p class=""note"">Vì đang dùng test env, hệ thống chưa nhận real webhook. Dùng nút bên dưới để mock thanh toán thành công.</p>
    <div style=""margin-top: 12px;"">
      <button class=""btn btn-pay"" id=""btnPay"" onclick=""simulatePayment('{orderId}')"">✓ Đã thanh toán thật (Mock Webhook)</button>
      <button class=""btn btn-cancel"" onclick=""simulateCancel('{orderId}')"">✗ Hủy thanh toán</button>
    </div>
  </div>

  <script>
    async function simulatePayment(orderId) {{
      const btn = document.getElementById('btnPay');
      btn.disabled = true;
      btn.textContent = '⏳ Đang xử lý...';
      try {{
        const res = await fetch('/api/debug/sepay/mock-webhook', {{
          method: 'POST',
          headers: {{ 'Content-Type': 'application/json' }},
          body: JSON.stringify({{ orderId }})
        }});
        const data = await res.json();
        if (data.status === 'deposit_marked_paid') {{
          document.getElementById('statusDot').className = 'status-dot paid';
          document.getElementById('statusText').textContent = 'Đã thanh toán thành công!';
          btn.textContent = '✓ Đã xác nhận PAID';
          btn.style.background = '#065f46';
        }} else {{
          btn.textContent = data.status;
        }}
      }} catch(e) {{
        btn.textContent = 'Lỗi: ' + e.message;
      }}
    }}

    async function simulateCancel(orderId) {{
      await fetch('/api/debug/sepay/mock-webhook', {{
        method: 'POST',
        headers: {{ 'Content-Type': 'application/json' }},
        body: JSON.stringify({{ orderId, status: 'cancelled' }})
      }});
      document.getElementById('statusDot').className = 'status-dot';
      document.getElementById('statusText').textContent = 'Đã hủy thanh toán';
    }}
  </script>
</body>
</html>";

        return Content(html, "text/html; charset=utf-8");
    }

    private bool IsDebugEnabled()
    {
        return _env.IsDevelopment()
            || string.Equals(Environment.GetEnvironmentVariable("ENABLE_DEBUG"), "true", StringComparison.OrdinalIgnoreCase);
    }
}
