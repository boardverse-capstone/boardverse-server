using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminModerationController : BaseApiController
    {
        private readonly IAdminModerationService _adminModerationService;
        private readonly ICoolingOffService _coolingOffService;
        private readonly IPlayerRiskQueryService _riskQueryService;
        private readonly IPlayerAlertService _alertService;
        private readonly IPlayerRiskScoreService _riskScoreService;
        private readonly LegacyBookingCleanupMetricsStore _legacyBookingMetrics;

        public AdminModerationController(
            IAdminModerationService adminModerationService,
            ICoolingOffService coolingOffService,
            IPlayerRiskQueryService riskQueryService,
            IPlayerAlertService alertService,
            IPlayerRiskScoreService riskScoreService,
            LegacyBookingCleanupMetricsStore legacyBookingMetrics)
        {
            _adminModerationService = adminModerationService;
            _coolingOffService = coolingOffService;
            _riskQueryService = riskQueryService;
            _alertService = alertService;
            _riskScoreService = riskScoreService;
            _legacyBookingMetrics = legacyBookingMetrics;
        }

        /// <summary>
        /// Truy xuất lịch sử biến động điểm Karma (phân trang, lọc user/vi phạm/thời gian). [Role: Admin]
        /// </summary>
        /// <param name="userId">Lọc theo mã người dùng.</param>
        /// <param name="violationCategory">Lọc theo nhóm hành vi vi phạm (NoShow, LateDepositCancel, KickedFromLobby, CrossRating, AdminManual, AdminWarning).</param>
        /// <param name="fromUtc">Thời điểm bắt đầu (UTC).</param>
        /// <param name="toUtc">Thời điểm kết thúc (UTC).</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 20).</param>
        /// <response code="200">Danh sách karma logs phân trang.</response>
        /// <response code="400">Tham số violationCategory không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("karma-logs")]
        public async Task<IActionResult> GetKarmaLogs(
            [FromQuery] Guid? userId,
            [FromQuery] string? violationCategory,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            KarmaViolationCategory? categoryFilter = null;
            if (!string.IsNullOrWhiteSpace(violationCategory))
            {
                if (!System.Enum.TryParse<KarmaViolationCategory>(violationCategory, true, out var parsed))
                {
                    throw new BadRequestException(ApiErrorMessages.AdminModeration.InvalidViolationCategoryFilter);
                }

                categoryFilter = parsed;
            }

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await _adminModerationService.GetKarmaLogsAsync(
                userId,
                categoryFilter,
                fromUtc,
                toUtc,
                pagination);

            return NewResponse(200, ApiSuccessMessages.AdminModeration.KarmaLogsRetrieved, result);
        }

        /// <summary>
        /// Danh sách tài khoản có Karma dưới ngưỡng an toàn (&lt; 50). [Role: Admin]
        /// </summary>
        /// <response code="200">Danh sách cảnh báo karma thấp.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("users/alerts")]
        public async Task<IActionResult> GetUserKarmaAlerts()
        {
            var result = await _adminModerationService.GetKarmaAlertsAsync();
            return NewResponse(200, ApiSuccessMessages.AdminModeration.KarmaAlertsRetrieved, result);
        }

        /// <summary>
        /// Thực hiện chế tài thủ công (WARNING / SUSPEND / BAN). [Role: Admin]
        /// </summary>
        /// <param name="id">Mã người dùng bị xử phạt.</param>
        /// <param name="request">actionType, durationDays (khi SUSPEND), reason.</param>
        /// <response code="200">Chế tài đã được áp dụng.</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc thiếu durationDays khi SUSPEND.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin hoặc mục tiêu là Admin.</response>
        /// <response code="404">Không tìm thấy người dùng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("users/{id:guid}/punish")]
        public async Task<IActionResult> PunishUser(Guid id, [FromBody] AdminPunishUserRequestDto request)
        {
            var adminUserId = GetUserIdFromClaims();
            var result = await _adminModerationService.PunishUserAsync(adminUserId, id, request);
            return NewResponse(200, ApiSuccessMessages.AdminModeration.PunishmentApplied, result);
        }

        /// <summary>
        /// Điều chỉnh điểm Karma thủ công và ghi nhật ký Admin. [Role: Admin]
        /// </summary>
        /// <param name="id">Mã người dùng được điều chỉnh.</param>
        /// <param name="request">amount (±), reason.</param>
        /// <response code="200">Karma đã cập nhật kèm karma log.</response>
        /// <response code="400">amount = 0 hoặc dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy profile người dùng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("users/{id:guid}/adjust-karma")]
        public async Task<IActionResult> AdjustKarma(Guid id, [FromBody] AdminAdjustKarmaRequestDto request)
        {
            var adminUserId = GetUserIdFromClaims();
            var result = await _adminModerationService.AdjustKarmaAsync(adminUserId, id, request);
            return NewResponse(200, ApiSuccessMessages.AdminModeration.KarmaAdjusted, result);
        }

        /// <summary>
        /// Lấy danh sách user đang cooling-off. [Role: Admin]
        /// </summary>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 20).</param>
        /// <response code="200">Danh sách user cooling-off.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("cooling-off")]
        public async Task<IActionResult> GetCoolingOffUsers(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await _adminModerationService.GetCoolingOffUsersAsync(pagination);
            return NewResponse(200, ApiSuccessMessages.AdminModeration.CoolingOffUsersRetrieved, result);
        }

        /// <summary>
        /// Release cooling-off cho một user. [Role: Admin]
        /// </summary>
        /// <param name="userId">Mã người dùng cần release.</param>
        /// <param name="dto">Lý do release cooling-off.</param>
        /// <response code="200">Đã release cooling-off thành công.</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc user không trong trạng thái cooling-off.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy ví của người dùng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("cooling-off/{userId:guid}/release")]
        public async Task<IActionResult> ReleaseCoolingOff(Guid userId, [FromBody] ReleaseCoolingOffRequestDto dto)
        {
            var adminUserId = GetUserIdFromClaims();
            var result = await _adminModerationService.ReleaseCoolingOffAsync(adminUserId, userId, dto.Reason);
            return NewResponse(200, ApiSuccessMessages.AdminModeration.CoolingOffReleased, result);
        }

        /// <summary>
        /// BR-NEW-10 §XI.2 — Admin manually extend cooling-off thêm N ngày cho 1 user.
        /// Dùng cho customer support hoặc escalation thủ công.
        /// Ghi audit log với <c>AdminActionType.AccountStatusChange</c> + metadata JSON.
        /// [Role: Admin]
        /// </summary>
        /// <param name="userId">Mã người dùng cần extend cooling-off.</param>
        /// <param name="dto">AdditionalDays (1..90) + Reason (≥ 10 ký tự).</param>
        /// <response code="200">Extend cooling-off thành công, trả newExpiresAt.</response>
        /// <response code="400">AdditionalDays ngoài khoảng hoặc Reason quá ngắn.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy ví của người dùng.</response>
        /// <response code="409">User không đang trong cooling-off.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("cooling-off/{userId:guid}/extend")]
        public async Task<IActionResult> ExtendCoolingOff(Guid userId, [FromBody] ExtendCoolingOffRequestDto dto)
        {
            if (!ModelState.IsValid)
            {
                return NewResponse(400, ApiErrorMessages.System.ReservationInvalidRequest, null);
            }

            var adminUserId = GetUserIdFromClaims();
            await _coolingOffService.ExtendAsync(adminUserId, userId, dto.AdditionalDays, dto.Reason);

            var response = new ExtendCoolingOffResponseDto
            {
                UserId = userId,
                AdditionalDays = dto.AdditionalDays,
                Reason = dto.Reason.Trim(),
                ExtendedBy = adminUserId,
                ExtendedAt = DateTime.UtcNow,
                NewExpiresAt = DateTime.UtcNow.AddDays(dto.AdditionalDays) // rough estimate; actual value logged in service
            };

            return NewResponse(200, "Đã gia hạn cooling-off thành công.", response);
        }

        /// <summary>
        /// BR-RISK-09 — Admin xem risk detail của 1 user (RiskScore, RiskMultiplier, Signals, CoolingOff).
        /// User bình thường KHÔNG được gọi endpoint này.
        /// [Role: Admin]
        /// </summary>
        /// <param name="userId">Mã người dùng cần xem risk detail.</param>
        /// <response code="200">Risk detail (admin-only).</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="404">Không tìm thấy ví của người dùng.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("players/{userId:guid}/risk")]
        public async Task<IActionResult> GetPlayerRiskDetail(Guid userId)
        {
            var result = await _riskQueryService.GetPlayerRiskDetailAsync(userId);
            return NewResponse(200, "Lấy risk detail thành công.", result);
        }
    /// <summary>
        /// A-03 (BR-RISK-05): Lấy lịch sử admin actions của 1 user (audit log vĩnh viễn).
        /// Bao gồm: Warning, Suspend, Ban, AdminCredit/Debit, RiskScoreReset, VerifyRequired,
        /// CoolingOffExtend, MultiAccountConfirmed, PlayedTimeDisputed/Overridden. [Role: Admin]
        /// </summary>
        /// <param name="userId">Mã người dùng (optional — bỏ trống để xem tất cả).</param>
        /// <param name="actionType">Lọc theo loại action (optional).</param>
        /// <param name="fromUtc">Mốc bắt đầu UTC (optional).</param>
        /// <param name="toUtc">Mốc kết thúc UTC (optional).</param>
        /// <param name="pageNumber">Trang (default 1).</param>
        /// <param name="pageSize">Kích thước trang (default 20).</param>
        /// <response code="200">Danh sách lịch sử admin action phân trang.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền Admin.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("users/action-history")]
        public async Task<IActionResult> GetPlayerActionHistory(
            [FromQuery] Guid? userId,
            [FromQuery] string? actionType,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            AdminActionType? actionTypeFilter = null;
            if (!string.IsNullOrWhiteSpace(actionType))
            {
                if (!System.Enum.TryParse<AdminActionType>(actionType, true, out var parsed))
                {
                    throw new BadRequestException(ApiErrorMessages.Controller.InvalidQueryParameter("actionType", string.Join(", ", Enum.GetNames<AdminActionType>())));
                }
                actionTypeFilter = parsed;
            }

            var query = new PlayerActionHistoryQuery
            {
                UserId = userId,
                ActionType = actionTypeFilter,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            var result = await _adminModerationService.GetPlayerActionHistoryAsync(query);
            return NewResponse(200, "Lấy lịch sử admin action thành công.", result);
        }

        /// <summary>
        /// R-01 (BR-RISK-02): Lấy danh sách PlayerAlert có phân trang, filter. [Role: Admin]
        /// </summary>
        [HttpGet("alerts")]
        public async Task<IActionResult> GetPlayerAlerts(
            [FromQuery] Guid? userId,
            [FromQuery] string? alertType,
            [FromQuery] string? severity,
            [FromQuery] string? status,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            PlayerAlertType? alertTypeFilter = null;
            if (!string.IsNullOrWhiteSpace(alertType))
            {
                if (!System.Enum.TryParse<PlayerAlertType>(alertType, true, out var parsed))
                {
                    throw new BadRequestException(ApiErrorMessages.Controller.InvalidQueryParameter("alertType", string.Join(", ", Enum.GetNames<PlayerAlertType>())));
                }
                alertTypeFilter = parsed;
            }

            PlayerAlertSeverity? severityFilter = null;
            if (!string.IsNullOrWhiteSpace(severity))
            {
                if (!System.Enum.TryParse<PlayerAlertSeverity>(severity, true, out var parsed))
                {
                    throw new BadRequestException(ApiErrorMessages.Controller.InvalidQueryParameter("severity", string.Join(", ", Enum.GetNames<PlayerAlertSeverity>())));
                }
                severityFilter = parsed;
            }

            PlayerAlertStatus? statusFilter = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!System.Enum.TryParse<PlayerAlertStatus>(status, true, out var parsed))
                {
                    throw new BadRequestException(ApiErrorMessages.Controller.InvalidQueryParameter("status", string.Join(", ", Enum.GetNames<PlayerAlertStatus>())));
                }
                statusFilter = parsed;
            }

            var query = new PlayerAlertQuery
            {
                UserId = userId,
                AlertType = alertTypeFilter,
                Severity = severityFilter,
                Status = statusFilter,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            var result = await _alertService.GetPagedAsync(query);
            return NewResponse(200, "Lấy danh sách alert thành công.", result);
        }

        /// <summary>
        /// R-01: Lấy metrics dashboard alerts (Open/Critical counts). [Role: Admin]
        /// </summary>
        [HttpGet("alerts/metrics")]
        public async Task<IActionResult> GetAlertMetrics()
        {
            var metrics = await _alertService.GetMetricsAsync();
            return NewResponse(200, "Lấy alert metrics thành công.", metrics);
        }

        /// <summary>
        /// R-01: Admin đánh dấu alert đã xem (status Acknowledged). [Role: Admin]
        /// </summary>
        [HttpPost("alerts/{alertId:guid}/acknowledge")]
        public async Task<IActionResult> AcknowledgeAlert(Guid alertId)
        {
            var adminUserId = GetUserIdFromClaims();
            var result = await _alertService.AcknowledgeAsync(alertId, adminUserId);
            return NewResponse(200, "Đã acknowledge alert.", result);
        }

        /// <summary>
        /// R-01: Admin resolve alert (sau khi xử lý — warn/suspend/ban). [Role: Admin]
        /// </summary>
        [HttpPost("alerts/{alertId:guid}/resolve")]
        public async Task<IActionResult> ResolveAlert(Guid alertId, [FromBody] AlertResolveRequestDto request)
        {
            var adminUserId = GetUserIdFromClaims();
            var result = await _alertService.ResolveAsync(alertId, adminUserId, request.Note);
            return NewResponse(200, "Đã resolve alert.", result);
        }

        /// <summary>
        /// R-01: Admin dismiss alert (false positive). [Role: Admin]
        /// </summary>
        [HttpPost("alerts/{alertId:guid}/dismiss")]
        public async Task<IActionResult> DismissAlert(Guid alertId, [FromBody] AlertResolveRequestDto request)
        {
            var adminUserId = GetUserIdFromClaims();
            var result = await _alertService.DismissAsync(alertId, adminUserId, request.Note);
            return NewResponse(200, "Đã dismiss alert.", result);
        }

        /// <summary>
        /// BR-RISK-11: Lấy lịch sử riskScore 30 ngày gần nhất của 1 user (chart trend). [Role: Admin]
        /// </summary>
        [HttpGet("players/{userId:guid}/risk-history")]
        public async Task<IActionResult> GetPlayerRiskHistory(
            Guid userId,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc)
        {
            var toDate = toUtc.HasValue ? DateOnly.FromDateTime(toUtc.Value) : DateOnly.FromDateTime(DateTime.UtcNow);
            var fromDate = fromUtc.HasValue ? DateOnly.FromDateTime(fromUtc.Value) : toDate.AddDays(-30);

            var history = await _riskScoreService.GetHistoryAsync(userId, fromDate, toDate);
            return NewResponse(200, "Lấy lịch sử risk score thành công.",
                new { UserId = userId, FromDate = fromDate, ToDate = toDate, Items = history });
        }

        /// <summary>
        /// GAP-10: Metrics cho <c>LegacyBookingCleanupJob</c> — last-run + counters. [Role: Admin]
        /// Dùng để monitor Phase 1 cleanup job trước khi tắt `/api/bookings/*` ở Phase 3.
        /// </summary>
        /// <response code="200">Snapshot metrics: lastRunAtUtc, lastBookingsProcessed, totalRuns, totalBookingsProcessed, lastDurationMs.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có role Admin.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("legacy-booking/cleanup-stats")]
        public IActionResult GetLegacyBookingCleanupStats()
        {
            var metrics = _legacyBookingMetrics.Snapshot();
            return NewResponse(200, "Lấy legacy booking cleanup stats thành công.", metrics);
        }
    }
}
