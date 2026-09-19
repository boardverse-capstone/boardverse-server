using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
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
    private readonly ISplitBillService _splitBillService;
    private readonly ISePayClient _sePayClient;
    private readonly IHostEnvironment _env;
    private readonly ILogger<PaymentController> _logger;

    // SePay HMAC headers — giá trị giữ nguyên, key case-insensitive.
    private const string HeaderSignature = "X-SePay-Signature";
    private const string HeaderTimestamp = "X-SePay-Timestamp";
    private const string AuthorizationHeader = "Authorization";

    public PaymentController(
        IPaymentService paymentService,
        IBookingDepositService depositService,
        IManualPaymentService manualPaymentService,
        ISplitBillService splitBillService,
        ISePayClient sePayClient,
        IHostEnvironment env,
        ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _depositService = depositService;
        _manualPaymentService = manualPaymentService;
        _splitBillService = splitBillService;
        _sePayClient = sePayClient;
        _env = env;
        _logger = logger;
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
            ?? throw new NotFoundException(ApiErrorMessages.Payment.DepositNotFoundById(depositId));

        var userId = GetUserIdFromClaims();
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

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
            throw new ForbiddenException(ApiErrorMessages.Payment.DepositForbidden);
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
            ?? throw new NotFoundException(ApiErrorMessages.Payment.DepositNotFoundByOrderId(orderId));

        var userId = GetUserIdFromClaims();
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        bool authorized = userRole switch
        {
            "Admin" => true,
            "Manager" => deposit.CafeManagerId == userId,
            "Player" => deposit.UserId == userId,
            _ => false
        };
        if (!authorized)
        {
            throw new ForbiddenException(ApiErrorMessages.Payment.DepositForbidden);
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
        var actorUserId = GetUserIdFromClaims();
        var actorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var result = await _paymentService.RefundDepositAsync(request.DepositId, request.Reason, actorUserId, actorRole);
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
    [Authorize(Roles = "Manager,CafeStaff,Admin")]
    public async Task<IActionResult> CreateSessionPayment([FromBody] CreateSessionPaymentRequestDto request)
    {
        var actorUserId = GetUserIdFromClaims();
        var actorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var result = await _paymentService.CreateSessionPaymentAsync(request, actorUserId, actorRole);
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
    [Authorize(Roles = "Manager,CafeStaff,Admin")]
    public async Task<IActionResult> RegenerateSessionQr(Guid sessionId)
    {
        var actorUserId = GetUserIdFromClaims();
        var actorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var result = await _paymentService.RegenerateSessionQrAsync(sessionId, actorUserId, actorRole);
        return this.NewResponse(200, "Tạo lại QR thanh toán phiên chơi thành công.", result);
    }

    // ============================================================
    // MANUAL PAYMENT ENDPOINT
    // ============================================================

    /// <summary>
    /// Staff xác nhận thanh toán thủ công khi cả SePay và VietQR đều không khả dụng.
    /// Use case: Khách thanh toán tiền mặt trực tiếp cho POS; hoặc SePay + VietQR đều timeout.
    /// BR-18: Xử lý sự cố vận hành — phiếu thu tiền mặt thay vì QR.
    /// Chỉ hỗ trợ SESSION (M6). Deposit có endpoint SePay riêng — staff không thể tự ý confirm DEPOSIT.
    /// [Role: Manager — chủ quán; CafeStaff — đã gắn quán; Admin bypass ownership.]
    /// </summary>
    /// <param name="request">Thông tin thanh toán thủ công.</param>
    /// <response code="200">Xác nhận thành công.</response>
    /// <response code="400">Thông tin không hợp lệ / session không ở UNPAID / amount mismatch.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Manager/CafeStaff của cafe.</response>
    /// <response code="404">Không tìm thấy session.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("manual-confirm")]
    [Authorize(Roles = "Manager,CafeStaff,Admin")]
    public async Task<IActionResult> ManualConfirmPayment([FromBody] ManualPaymentConfirmRequestDto request)
    {
        var staffId = GetUserIdFromClaims();
        var actorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var result = await _manualPaymentService.ConfirmManualPaymentAsync(request, staffId, actorRole);
        return this.NewResponse(200, "Xác nhận thanh toán thủ công thành công.", result);
    }

    // ============================================================
    // SPLIT BILL (PER-MEMBER PAYMENT) ENDPOINTS
    // ============================================================

    /// <summary>
    /// Webhook xử lý thanh toán QR cho thành viên cụ thể trong group session.
    /// GAP FIX: Thêm signature verification (giống deposit/session webhook).
    /// [Public - SePay webhook]
    /// </summary>
    /// <response code="200">Xử lý thành công.</response>
    /// <response code="400">Dữ liệu webhook không hợp lệ.</response>
    /// <response code="401">Signature không hợp lệ.</response>
    /// <response code="404">Không tìm thấy thành viên.</response>
    /// <response code="409">Số tiền không khớp.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("sepay/webhook/member-payment")]
    [AllowAnonymous]
    public async Task<IActionResult> ProcessMemberPaymentWebhook()
    {
        // GAP FIX #1: Verify webhook signature trước khi xử lý payload.
        // Dùng ISePayClient.VerifyWebhookAsync — cùng logic verify như deposit/session webhook.
        // Pattern y hệt SePayWebhookController.ReceiveWebhook.
        Request.EnableBuffering();
        var rawBody = await new StreamReader(Request.Body, leaveOpen: true)
            .ReadToEndAsync(HttpContext.RequestAborted);
        Request.Body.Position = 0;

        var signature = ExtractSignatureFromHeaders(Request.Headers);
        var timestamp = Request.Headers[HeaderTimestamp].ToString();
        var verificationRequest = new SePayWebhookVerificationRequest(
            Signature: signature,
            Timestamp: timestamp,
            RawBody: rawBody);

        // G3 FIX: Align with SePayWebhookController — skip verification ONLY in Development.
        // In Staging/Production, always verify via IPaymentService.VerifyWebhookRequestAsync
        // (same pattern as SePayWebhookController.ReceiveWebhook).
        // This ensures WebhookAuthType.None is also rejected in Staging, not just Production.
        if (_env.IsDevelopment())
        {
            // Development: skip verification for convenience during local testing.
            _logger.LogDebug("Member payment webhook: signature verification skipped in Development.");
        }
        else
        {
            var (isValid, errorMessage) = await _paymentService.VerifyWebhookRequestAsync(
                verificationRequest, HttpContext.RequestAborted);

            if (!isValid)
            {
                _logger.LogWarning(
                    "Member payment webhook signature verification failed. Error={Error}",
                    errorMessage);
                return Unauthorized(new { status = "error", message = errorMessage ?? "Invalid signature." });
            }
        }

        // Parse payload từ rawBody sau khi đã đọc raw body cho signature.
        MemberPaymentWebhookDto? webhook;
        try
        {
            webhook = System.Text.Json.JsonSerializer.Deserialize<MemberPaymentWebhookDto>(
                rawBody,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (webhook == null)
            {
                _logger.LogWarning("Member payment webhook payload empty or invalid JSON.");
                return BadRequest(new { status = "error", message = "Invalid payload." });
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Member payment webhook JSON parse failed.");
            return BadRequest(new { status = "error", message = "Invalid JSON." });
        }

        await _splitBillService.ProcessMemberQrWebhookAsync(webhook, HttpContext.RequestAborted);
        return Ok();
    }

    /// <summary>
    /// Staff xác nhận thanh toán QR đã được chuyển khoản cho một thành viên.
    /// Use case: Khách đã quét QR và chuyển tiền, staff xác nhận đã nhận được.
    /// [Role: Manager — chủ quán; CafeStaff — đã gắn quán; Admin bypass.]
    /// </summary>
    /// <param name="sessionId">Mã phiên chơi.</param>
    /// <param name="memberId">Mã thành viên.</param>
    /// <param name="notes">Ghi chú (optional).</param>
    /// <response code="200">Xác nhận thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không đủ quyền.</response>
    /// <response code="404">Không tìm thấy phiên hoặc thành viên.</response>
    /// <response code="409">Thành viên đã thanh toán.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("confirm-member-qr")]
    [Authorize(Roles = "Manager,CafeStaff,Admin")]
    public async Task<IActionResult> ConfirmMemberQr(
        [FromQuery] Guid sessionId,
        [FromQuery] Guid memberId,
        [FromQuery] string? notes = null)
    {
        var staffId = GetUserIdFromClaims();
        var actorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var result = await _splitBillService.ConfirmMemberQrAsync(
            sessionId, memberId, staffId, actorRole, notes, HttpContext.RequestAborted);
        return this.NewResponse(200, "Xác nhận thanh toán QR thành công.", result);
    }

    /// <summary>
    /// Lấy thông tin QR code thanh toán của một thành viên để hiển thị trên mobile.
    /// - Staff/Admin: xem QR của bất kỳ thành viên nào.
    /// - Player: chỉ xem được QR của chính mình (thành viên thuộc session mà player đang tham gia).
    /// [Role: Staff — xem QR bất kỳ; Player — chỉ QR của mình; Admin — xem tất cả.]
    /// </summary>
    /// <param name="memberId">Mã thành viên cần lấy QR.</param>
    /// <response code="200">Lấy thông tin QR thành công. Trả về null nếu thành viên chưa được tạo QR.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Player không có quyền xem QR của thành viên khác.</response>
    /// <response code="404">Không tìm thấy thành viên.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("split-bill/members/{memberId:guid}/qr")]
    [Authorize]
    public async Task<IActionResult> GetMemberQr(Guid memberId)
    {
        var requesterId = GetUserIdFromClaims();
        var requesterRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        var result = await _splitBillService.GetMemberQrAsync(
            memberId, requesterId, requesterRole, HttpContext.RequestAborted);

        if (result == null)
        {
            return this.NewResponse(404, "Không tìm thấy thành viên.", result);
        }

        return this.NewResponse(200, "Lấy thông tin QR thành công.", result);
    }

    /// <summary>
    /// Extract signature theo mode SePay đang dùng:
    /// - HMAC-SHA256: header <c>X-SePay-Signature</c> (VD: <c>sha256=abc...</c>).
    /// - API Key: header <c>Authorization</c> với format <c>Apikey &lt;token&gt;</c>.
    /// Caller đã pre-select mode qua SePayAccount.WebhookAuthType.
    /// </summary>
    private static string? ExtractSignatureFromHeaders(IHeaderDictionary headers)
    {
        var sigHeader = headers[HeaderSignature].ToString();
        if (!string.IsNullOrWhiteSpace(sigHeader))
        {
            return sigHeader.Trim();
        }

        var authHeader = headers[AuthorizationHeader].ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            const string apiKeyPrefix = "Apikey ";
            if (authHeader.StartsWith(apiKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return authHeader.Substring(apiKeyPrefix.Length).Trim();
            }
            return authHeader.Trim();
        }

        return null;
    }
}
