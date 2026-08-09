using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Debug/test endpoints cho Session Payment (manager thanh toán hóa đơn phiên chơi tại POS).
/// Chỉ khả dụng khi ASPNETCORE_ENVIRONMENT=Development (gate theo C9 trong sepay-payment-flow.mdc).
/// Trên Production (Render) sẽ trả 404 — không cần lo lộ data.
/// </summary>
[ApiController]
[Route("api/debug/session-payment")]
public class DebugSessionPaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly BoardVerseDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DebugSessionPaymentController> _logger;

    public DebugSessionPaymentController(
        IPaymentService paymentService,
        BoardVerseDbContext db,
        IWebHostEnvironment env,
        ILogger<DebugSessionPaymentController> logger)
    {
        _paymentService = paymentService;
        _db = db;
        _env = env;
        _logger = logger;
    }

    private bool IsDebugEnabled() => _env.IsDevelopment();

    /// <summary>
    /// Tạo nhanh 1 ActiveSession status=Unpaid gắn vào cafe (mặc định: cafe đầu tiên trong DB).
    /// Trả sessionId để dùng cho các bước test tiếp theo.
    /// Idempotent: nếu đã có session UNPAID cùng cafe + host → trả về session đó.
    /// </summary>
    /// <param name="cafeId">Optional override chỉ định cafe cụ thể (cần có SePayAccountId configured).</param>
    /// <param name="amount">TotalAmount của session (mặc định 85000).</param>
    [HttpPost("seed")]
    public async Task<IActionResult> SeedSession(
        [FromQuery] Guid? cafeId = null,
        [FromQuery] decimal? amount = null)
    {
        if (!IsDebugEnabled()) return NotFound();

        Cafe? cafe;
        if (cafeId.HasValue)
        {
            cafe = await _db.Cafes.FirstOrDefaultAsync(c => c.Id == cafeId.Value);
            if (cafe == null)
                return BadRequest($"Không tìm thấy cafe với Id={cafeId.Value}.");
        }
        else
        {
            cafe = await _db.Cafes.OrderBy(c => c.Id).FirstOrDefaultAsync();
            if (cafe == null)
                return BadRequest("Không tìm thấy Cafe nào trong DB. Hãy seed cafe trước.");
        }

        var host = await _db.Users.OrderBy(u => u.CreatedAt).FirstOrDefaultAsync();
        if (host == null)
            return BadRequest("Không tìm thấy User nào trong DB. Hãy seed user trước.");

        var game = await _db.GameTemplates.OrderBy(g => g.Id).FirstOrDefaultAsync();
        if (game == null)
            return BadRequest("Không tìm thấy GameTemplate nào trong DB. Hãy seed game trước.");

        var totalAmount = amount ?? 85000m;

        // Reuse existing Unpaid session if present (same cafe + host + game).
        var existing = await _db.ActiveSessions
            .Where(s => s.CafeId == cafe.Id && s.HostId == host.Id
                && s.GameTemplateId == game.Id && s.Status == GroupSessionStatus.Unpaid)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.TotalAmount = totalAmount;
            existing.Subtotal = totalAmount;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "[DebugSessionPayment] Reused existing Unpaid session {SessionId} (Cafe={CafeId}, Amount={Amount})",
                existing.Id, cafe.Id, totalAmount);

            return Ok(new
            {
                reused = true,
                sessionId = existing.Id,
                cafeId = cafe.Id,
                cafeName = cafe.Name,
                cafeHasSePayAccount = cafe.SePayAccountId.HasValue,
                cafeSePayAccountId = cafe.SePayAccountId,
                hostId = host.Id,
                gameTemplateId = game.Id,
                amount = totalAmount,
                status = existing.Status.ToString()
            });
        }

        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafe.Id,
            HostId = host.Id,
            GameTemplateId = game.Id,
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            Status = GroupSessionStatus.Unpaid,
            Subtotal = totalAmount,
            TotalAmount = totalAmount,
            DepositAppliedAmount = 0m,
            SurchargeFine = 0m,
            PenaltyAmount = 0m,
            TotalMinutesPlayed = 60,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.ActiveSessions.Add(session);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "[DebugSessionPayment] Seeded Unpaid session {SessionId} (Cafe={CafeId}, Amount={Amount})",
            session.Id, cafe.Id, totalAmount);

        return Ok(new
        {
            reused = false,
            sessionId = session.Id,
            cafeId = cafe.Id,
            cafeName = cafe.Name,
            cafeHasSePayAccount = cafe.SePayAccountId.HasValue,
            cafeSePayAccountId = cafe.SePayAccountId,
            hostId = host.Id,
            gameTemplateId = game.Id,
            amount = totalAmount,
            status = session.Status.ToString()
        });
    }

    /// <summary>
    /// Gọi thẳng PaymentService.CreateSessionPaymentAsync — bypass JWT/role.
    /// Trả paymentUrl + qrImageUrl để test QR flow.
    /// [Role: Public — chỉ khả dụng khi ASPNETCORE_ENVIRONMENT=Development.]
    /// </summary>
    /// <param name="request">SessionId của ActiveSession status=Unpaid.</param>
    /// <response code="200">Tạo QR thành công, trả paymentUrl + qrImageUrl.</response>
    /// <response code="404">Endpoint không khả dụng (Production env hoặc route sai).</response>
    /// <response code="500">Lỗi từ service (vd. cafe chưa config SePay, gateway lỗi, …).</response>
    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateSessionPaymentRequestDto request)
    {
        if (!IsDebugEnabled()) return NotFound();

        try
        {
            // Bypass ownership check: truyền role Admin để VerifyCafeOperatorAsync return ngay.
            var result = await _paymentService.CreateSessionPaymentAsync(request, Guid.Empty, "Admin");
            return Ok(new
            {
                sessionId = result.SessionId,
                paymentUrl = result.PaymentUrl,
                qrImageUrl = result.QrImageUrl,
                orderId = result.OrderId,
                amount = result.Amount,
                gateway = result.Gateway,
                requiresManualConfirmation = result.RequiresManualConfirmation,
                status = result.Status
            });
        }
        catch (Exception ex)
        {
            // P0-Fix-#7: KHÔNG lộ internal details (exception type + inner exception message)
            // có thể chứa DB/provider/implementation details. Log đầy đủ phía server,
            // trả về generic message cho client.
            _logger.LogError(ex,
                "[DebugSessionPayment] Create session payment failed. SessionId={SessionId}",
                request.SessionId);
            return StatusCode(500, new
            {
                error = "InternalError",
                message = "Không thể tạo thanh toán phiên chơi. Vui lòng kiểm tra log server."
            });
        }
    }

    /// <summary>
    /// Mock SePay webhook success cho 1 session — đẩy status Unpaid → Paid.
    /// Sau khi gọi endpoint này, GET /api/v1/pos/sessions/{sessionId} sẽ thấy status=Paid.
    /// </summary>
    [HttpPost("mock-success")]
    public async Task<IActionResult> MockSuccess([FromQuery] Guid sessionId)
    {
        if (!IsDebugEnabled()) return NotFound();

        var session = await _db.ActiveSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null)
            return NotFound(new { message = $"Session {sessionId} không tồn tại." });

        if (session.Status != GroupSessionStatus.Unpaid)
            return Conflict(new
            {
                message = $"Session đang ở trạng thái {session.Status}, không thể mock success.",
                currentStatus = session.Status.ToString()
            });

        // Cập nhật trực tiếp thay vì gọi webhook (đơn giản, không cần HMAC).
        session.Status = GroupSessionStatus.Paid;
        session.PaidAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "[DebugSessionPayment] Mock-success session {SessionId} → Paid (Amount={Amount})",
            session.Id, session.TotalAmount);

        return Ok(new
        {
            sessionId = session.Id,
            status = session.Status.ToString(),
            paidAt = session.PaidAt,
            totalAmount = session.TotalAmount,
            message = "Session đã được mock chuyển sang Paid."
        });
    }

    /// <summary>
    /// Xem nhanh trạng thái hiện tại của session (bypass auth).
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] Guid sessionId)
    {
        if (!IsDebugEnabled()) return NotFound();

        var session = await _db.ActiveSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
            return NotFound(new { message = $"Session {sessionId} không tồn tại." });

        return Ok(new
        {
            sessionId = session.Id,
            cafeId = session.CafeId,
            hostId = session.HostId,
            status = session.Status.ToString(),
            totalAmount = session.TotalAmount,
            subtotal = session.Subtotal,
            orderId = session.OrderId,
            transferContent = session.TransferContent,
            qrUrl = session.TransferContent, // field alias for debug
            paidAt = session.PaidAt,
            startedAt = session.StartedAt,
            endedAt = session.EndedAt,
            updatedAt = session.UpdatedAt
        });
    }

    /// <summary>
    /// Health check: endpoint có khả dụng không.
    /// P0-Fix-#7: gate cả Ping endpoint — trước đây không có gate nên lộ env name ở Production.
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        if (!IsDebugEnabled()) return NotFound();

        return Ok(new
        {
            enabled = true,
            env = _env.EnvironmentName,
            timestamp = DateTime.UtcNow
        });
    }
}
