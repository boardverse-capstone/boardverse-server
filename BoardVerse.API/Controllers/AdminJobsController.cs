using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin: Manual trigger cho các background job. Dùng khi cần chạy job ngay
/// (test, recovery, hoặc scheduler bị delay). Endpoint này idempotent — gọi nhiều lần OK.
/// </summary>
[ApiController]
[Route("api/v1/admin/jobs")]
[Authorize(Roles = "Admin")]
public class AdminJobsController : BaseApiController
{
    private readonly IReservationService _reservationService;
    private readonly ITournamentService _tournamentService;
    private readonly IBookingDepositService _bookingDepositService;
    private readonly IWalletService _walletService;
    private readonly ISettlementService _settlementService;
    private readonly IFriendService _friendService;
    private readonly ISystemConfigurationProvider _configProvider;
    private readonly ILogger<AdminJobsController> _logger;

    public AdminJobsController(
        IReservationService reservationService,
        ITournamentService tournamentService,
        IBookingDepositService bookingDepositService,
        IWalletService walletService,
        ISettlementService settlementService,
        IFriendService friendService,
        ISystemConfigurationProvider configProvider,
        ILogger<AdminJobsController> logger)
    {
        _reservationService = reservationService;
        _tournamentService = tournamentService;
        _bookingDepositService = bookingDepositService;
        _walletService = walletService;
        _settlementService = settlementService;
        _friendService = friendService;
        _configProvider = configProvider;
        _logger = logger;
    }

    /// <summary>
    /// Trigger xử lý reservation đến deadline: viable/timeout (BR-LOBBY-02). [Role: Admin]
    /// </summary>
    /// <param name="batchSize">Số lượng xử lý mỗi lần (1-500, default 100).</param>
    /// <response code="200">Số reservation đã xử lý.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Tài khoản không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("reservations/process-deadlines")]
    public async Task<IActionResult> ProcessReservationDeadlines([FromQuery] int batchSize = 100)
    {
        var safe = Math.Clamp(batchSize, 1, 500);
        var count = await _reservationService.ProcessDeadlineReservationsAsync(
            DateTime.UtcNow, safe, HttpContext.RequestAborted);
        _logger.LogInformation("Admin manual trigger: ProcessDeadlineReservationsAsync → {Count} processed.", count);
        return this.NewResponse(200, $"Đã xử lý {count} reservation đến deadline.", new { processed = count });
    }

    /// <summary>
    /// Trigger xử lý lobby pendingCafeApproval quá 24h → expiredByCafe (BR-NEW-11). [Role: Admin]
    /// </summary>
    /// <param name="batchSize">Số lượng xử lý (1-500, default 100).</param>
    /// <response code="200">Số reservation đã expire.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("reservations/process-cafe-approval-expiry")]
    public async Task<IActionResult> ProcessCafeApprovalExpiry([FromQuery] int batchSize = 100)
    {
        var safe = Math.Clamp(batchSize, 1, 500);
        var count = await _reservationService.ProcessCafeApprovalExpiryAsync(
            DateTime.UtcNow, safe, HttpContext.RequestAborted);
        _logger.LogInformation("Admin manual trigger: ProcessCafeApprovalExpiryAsync → {Count} processed.", count);
        return this.NewResponse(200, $"Đã xử lý {count} reservation quá hạn cafe approval.", new { processed = count });
    }

    /// <summary>
    /// Trigger xử lý no-show sau scheduledTime + grace (BR §21A.9). [Role: Admin]
    /// </summary>
    /// <param name="batchSize">Số lượng xử lý (1-500, default 100).</param>
    /// <response code="200">Số reservation đã đánh no-show.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("reservations/process-no-show")]
    public async Task<IActionResult> ProcessNoShow([FromQuery] int batchSize = 100)
    {
        var safe = Math.Clamp(batchSize, 1, 500);
        var count = await _reservationService.ProcessNoShowAsync(
            DateTime.UtcNow, safe, HttpContext.RequestAborted);
        _logger.LogInformation("Admin manual trigger: ProcessNoShowAsync → {Count} processed.", count);
        return this.NewResponse(200, $"Đã đánh no-show {count} reservation.", new { processed = count });
    }

    /// <summary>
    /// Retry BVC capture cho các session PAID nhưng capture thất bại trước đó (GAP-9). [Role: Admin]
    /// </summary>
    /// <param name="batchSize">Số lượng retry (1-500, default 100).</param>
    /// <response code="200">Số BVC capture retry thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("reservations/process-bvc-capture-retry")]
    public async Task<IActionResult> ProcessBvcCaptureRetry([FromQuery] int batchSize = 100)
    {
        var safe = Math.Clamp(batchSize, 1, 500);
        var count = await _reservationService.ProcessBvcCaptureRetryAsync(
            DateTime.UtcNow, safe, HttpContext.RequestAborted);
        _logger.LogInformation("Admin manual trigger: ProcessBvcCaptureRetryAsync → {Count} processed.", count);
        return this.NewResponse(200, $"Đã retry {count} BVC capture.", new { processed = count });
    }

    /// <summary>
    /// Trigger expire booking deposits quá thời gian giữ chỗ (BR-06). [Role: Admin]
    /// </summary>
    /// <response code="200">Số deposit đã expire.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("deposits/process-expired")]
    public async Task<IActionResult> ProcessExpiredDeposits()
    {
        await _bookingDepositService.ProcessExpiredDepositsAsync();
        _logger.LogInformation("Admin manual trigger: ProcessExpiredDepositsAsync → processed.");
        return this.NewResponse(200, "Đã trigger expire booking deposits.", new { processed = true });
    }

    /// <summary>
    /// Trigger expire pending BVC top-ups quá timeout. [Role: Admin]
    /// </summary>
    /// <response code="200">Số top-up đã expire.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("wallet/expire-pending-topups")]
    public async Task<IActionResult> ExpirePendingTopUps()
    {
        var count = await _walletService.ExpirePendingTopUpsAsync();
        _logger.LogInformation("Admin manual trigger: ExpirePendingTopUpsAsync → {Count} processed.", count);
        return this.NewResponse(200, $"Đã expire {count} pending top-up.", new { processed = count });
    }

    /// <summary>
    /// Trigger auto-close tournament registrations quá hạn. [Role: Admin]
    /// </summary>
    /// <response code="200">Số tournament đã close registration.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("tournaments/auto-close-expired-registrations")]
    public async Task<IActionResult> AutoCloseExpiredRegistrations()
    {
        var count = await _tournamentService.AutoCloseExpiredRegistrationsAsync(
            DateTime.UtcNow, HttpContext.RequestAborted);
        _logger.LogInformation("Admin manual trigger: AutoCloseExpiredRegistrationsAsync → {Count} processed.", count);
        return this.NewResponse(200, $"Đã auto-close {count} tournament registration.", new { processed = count });
    }

    /// <summary>
    /// Trigger gửi tournament reminders (48h/24h trước start). [Role: Admin]
    /// </summary>
    /// <response code="200">Số reminder đã gửi.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("tournaments/send-reminders")]
    public async Task<IActionResult> SendTournamentReminders()
    {
        var count = await _tournamentService.SendTournamentRemindersAsync(
            DateTime.UtcNow, HttpContext.RequestAborted);
        _logger.LogInformation("Admin manual trigger: SendTournamentRemindersAsync → {Count} processed.", count);
        return this.NewResponse(200, $"Đã gửi {count} tournament reminder.", new { processed = count });
    }

    /// <summary>
    /// Trigger auto-mark no-show cho tournament participants quá grace period. [Role: Admin]
    /// </summary>
    /// <response code="200">Số participant đã mark no-show.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("tournaments/auto-mark-no-shows")]
    public async Task<IActionResult> AutoMarkNoShows()
    {
        var result = await _tournamentService.AutoMarkNoShowsAsync(HttpContext.RequestAborted);
        _logger.LogInformation(
            "Admin manual trigger: AutoMarkNoShowsAsync → {Marked} marked, KarmaPenalty={Penalty}.",
            result.TotalMarked, result.TotalKarmaPenalty);
        return this.NewResponse(200, $"Đã mark no-show {result.TotalMarked} participants.", result);
    }

    /// <summary>
    /// Trigger expire pending friend requests quá 30 ngày. [Role: Admin]
    /// </summary>
    /// <response code="200">Số friend request đã expire.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("friends/expire-old-pending-requests")]
    public async Task<IActionResult> ExpireOldPendingFriendRequests()
    {
        var count = await _friendService.ExpireOldPendingRequestsAsync();
        _logger.LogInformation("Admin manual trigger: ExpireOldPendingRequestsAsync → {Count} processed.", count);
        return this.NewResponse(200, $"Đã expire {count} friend request.", new { processed = count });
    }

    /// <summary>
    /// Invalidate cache system configuration. Dùng khi đổi config qua DB và muốn áp dụng ngay. [Role: Admin]
    /// </summary>
    /// <response code="200">Cache đã được invalidate.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("config/invalidate-cache")]
    public async Task<IActionResult> InvalidateConfigCache()
    {
        await _configProvider.InvalidateCacheAsync();
        _logger.LogInformation("Admin manual trigger: InvalidateCacheAsync → cleared.");
        return this.NewResponse(200, "Đã invalidate cache system configuration.", new { cleared = true });
    }

    /// <summary>
    /// Manual retry SePay transfer cho settlement bị Failed (W-06). Dùng khi settlement retry
    /// job không xử lý kịp hoặc admin cần trigger ngay cho 1 settlement cụ thể. [Role: Admin]
    /// </summary>
    /// <param name="cafeId">Mã cafe.</param>
    /// <param name="sessionId">Mã session (ActiveSession.Id).</param>
    /// <param name="activeSessionId">Mã ActiveSession (correlation id, có thể trùng sessionId cho BVC flow).</param>
    /// <response code="200">Settlement đã được release.</response>
    /// <response code="400">Session chưa PAID.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="404">Không tìm thấy settlement.</response>
    /// <response code="409">Cafe chưa config SePay hoặc điều kiện không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("settlement/release-session-deposit")]
    public async Task<IActionResult> ReleaseSessionDeposit(
        [FromQuery] Guid cafeId,
        [FromQuery] Guid sessionId,
        [FromQuery] Guid activeSessionId)
    {
        var result = await _settlementService.ReleaseSessionDepositAsync(cafeId, sessionId, activeSessionId);
        _logger.LogInformation(
            "Admin manual trigger: ReleaseSessionDepositAsync CafeId={CafeId} SessionId={SessionId} → Status={Status}.",
            cafeId, sessionId, result.Status);
        return this.NewResponse(200, $"Đã release settlement. Status={result.Status}.", result);
    }
}