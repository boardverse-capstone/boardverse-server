using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
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

        public AdminModerationController(IAdminModerationService adminModerationService)
        {
            _adminModerationService = adminModerationService;
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

            return NewResponse(200, "Karma logs retrieved successfully", result);
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
            return NewResponse(200, "User karma alerts retrieved successfully", result);
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
            return NewResponse(200, "User punishment applied successfully", result);
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
            return NewResponse(200, "User karma adjusted successfully", result);
        }
    }
}
