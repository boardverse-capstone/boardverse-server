using BoardVerse.Core.DTOs.Receipt;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    /// <summary>
    /// API for receipts and revenue reports.
    /// P-01: Receipt Generation
    /// P-02: Revenue Report
    /// </summary>
    [ApiController]
    [Route("api/v1")]
    public abstract class BaseReceiptController : BaseApiController { }

    /// <summary>
    /// Receipt generation and revenue report endpoints.
    /// P-01: Receipt Generation API — GET /api/v1/sessions/{sessionId}/receipt
    /// P-02: Revenue Report API — GET /api/v1/cafes/{cafeId}/revenue
    /// M2/C2.15: Member Receipt API — GET /api/v1/sessions/{sessionId}/members/{memberId}/receipt
    /// </summary>
    [Authorize(Roles = "Admin,Manager,CafeStaff,Player")]
    public class ReceiptController : BaseReceiptController
    {
        private readonly IReceiptService _receiptService;
        private readonly IActiveSessionRepository _activeSessionRepository;
        private readonly ICafeRepository _cafeRepository;

        public ReceiptController(
            IReceiptService receiptService,
            IActiveSessionRepository activeSessionRepository,
            ICafeRepository cafeRepository)
        {
            _receiptService = receiptService;
            _activeSessionRepository = activeSessionRepository;
            _cafeRepository = cafeRepository;
        }

        /// <summary>
        /// Lấy receipt cho một phiên chơi đã thanh toán. [Role: Admin, Manager, CafeStaff]
        /// P-01: Receipt Generation API
        /// </summary>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <response code="200">Receipt chi tiết.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền truy cập.</response>
        /// <response code="404">Không tìm thấy phiên chơi.</response>
        /// <response code="409">Phiên chưa được thanh toán.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("sessions/{sessionId:guid}/receipt")]
        public async Task<IActionResult> GetSessionReceipt(Guid sessionId)
        {
            var receipt = await _receiptService.GenerateSessionReceiptAsync(sessionId);
            return NewResponse(200, ApiSuccessMessages.Session.ReceiptGenerated, receipt);
        }

        /// <summary>
        /// Lấy receipt cho một thành viên cụ thể trong phiên chơi đã thanh toán.
        /// M2/C2.15 — Per-member receipt (Gap #32).
        /// <para>
        /// Quyền truy cập:
        /// </para>
        /// <list type="bullet">
        ///   <item>Admin: luôn pass.</item>
        ///   <item>Manager: phải là chủ quán của session.</item>
        ///   <item>CafeStaff: phải là staff của cafe của session.</item>
        ///   <item>Player: phải là chính member này (member.UserId == currentUser.Id) HOẶC là host của session.</item>
        /// </list>
        /// <para>
        /// Format: hiện tại chỉ hỗ trợ <c>json</c>. PDF/PNG sẽ được tích hợp ở release sau
        /// khi bổ sung QuestPDF dependency. Request <c>pdf</c>/<c>png</c> trả 400.
        /// </para>
        /// </summary>
        /// <param name="sessionId">Mã phiên chơi.</param>
        /// <param name="memberId">Mã thành viên trong phiên chơi.</param>
        /// <param name="format">Định dạng output: <c>json</c> (mặc định). <c>pdf</c>/<c>png</c> hiện không được hỗ trợ.</param>
        /// <response code="200">File receipt JSON cho member.</response>
        /// <response code="400">Format không hợp lệ (chỉ chấp nhận 'json').</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền truy cập receipt của member này:
        /// <list type="bullet">
        ///   <item>Player không phải member này và không phải host của session.</item>
        ///   <item>Manager không phải chủ quán (<c>cafe.ManagerId ≠ currentUser.Id</c>).</item>
        ///   <item>CafeStaff không phải staff của cafe của session.</item>
        ///   <item>Role khác ngoài Admin/Manager/CafeStaff/Player.</item>
        /// </list>
        /// </response>
        /// <response code="404">Không tìm thấy phiên chơi hoặc thành viên.</response>
        /// <response code="409">Phiên chưa được thanh toán.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("sessions/{sessionId:guid}/members/{memberId:guid}/receipt")]
        public async Task<IActionResult> GetMemberReceipt(
            Guid sessionId,
            Guid memberId,
            [FromQuery] string format = "json")
        {
            // ===== Permission check =====
            var actorUserId = GetUserIdFromClaims();
            var session = await _activeSessionRepository.GetByIdWithMembersAsync(sessionId, HttpContext.RequestAborted)
                ?? throw new NotFoundException(ApiErrorMessages.Pos.SessionNotFoundById(sessionId));

            var member = session.Members.FirstOrDefault(m => m.Id == memberId)
                ?? throw new NotFoundException(ApiErrorMessages.Receipt.MemberNotInSession(memberId));

            var isAdmin = User.IsInRole("Admin");
            var isManager = User.IsInRole("Manager");
            var isStaff = User.IsInRole("CafeStaff");
            var isPlayer = User.IsInRole("Player");

            if (!isAdmin)
            {
                if (isManager)
                {
                    var cafe = await _cafeRepository.GetActiveByIdAsync(session.CafeId, HttpContext.RequestAborted);
                    if (cafe == null || cafe.ManagerId != actorUserId)
                    {
                        throw new ForbiddenException(
                            ApiErrorMessages.Payment.ManualConfirmNotAuthorizedForCafe(session.CafeId));
                    }
                }
                else if (isStaff)
                {
                    var isStaffMember = await _cafeRepository.IsStaffMemberExistsAsync(session.CafeId, actorUserId);
                    if (!isStaffMember)
                    {
                        throw new ForbiddenException(
                            ApiErrorMessages.Payment.ManualConfirmNotAuthorizedForCafe(session.CafeId));
                    }
                }
                else if (isPlayer)
                {
                    // Player: phải là chính member này HOẶC host của session.
                    var isOwnMember = member.UserId.HasValue && member.UserId.Value == actorUserId;
                    var isHost = session.HostId == actorUserId;
                    if (!isOwnMember && !isHost)
                    {
                        throw new ForbiddenException(
                            ApiErrorMessages.Session.PlayerNotInSession);
                    }
                }
                else
                {
                    throw new ForbiddenException(ApiErrorMessages.Controller.Forbidden);
                }
            }

            var receiptFile = await _receiptService.GenerateMemberReceiptAsync(
                sessionId, memberId, format, HttpContext.RequestAborted);

            // Return raw bytes via File() result.
            return File(receiptFile.Bytes, receiptFile.ContentType, receiptFile.FileName);
        }

        /// <summary>
        /// Lấy báo cáo doanh thu theo kỳ. [Role: Admin, Manager]
        /// P-02: Revenue Report API
        /// </summary>
        /// <param name="cafeId">Mã cafe.</param>
        /// <param name="startDate">Ngày bắt đầu (yyyy-MM-dd).</param>
        /// <param name="endDate">Ngày kết thúc (yyyy-MM-dd).</param>
        /// <param name="granularity">daily|weekly|monthly. Mặc định: daily.</param>
        /// <response code="200">Báo cáo doanh thu chi tiết.</response>
        /// <response code="400">Dữ liệu không hợp lệ (ngày không hợp lệ hoặc granularity không đúng).</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        /// <response code="403">Không có quyền truy cập.</response>
        /// <response code="404">Không tìm thấy cafe.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("cafes/{cafeId:guid}/revenue")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetRevenueReport(
            Guid cafeId,
            [FromQuery] DateOnly startDate,
            [FromQuery] DateOnly endDate,
            [FromQuery] string granularity = "daily")
        {
            if (endDate < startDate)
            {
                return NewResponse(400, ApiErrorMessages.System.DateRangeInvalid(startDate, endDate), null);
            }

            var validGranularities = new[] { "daily", "weekly", "monthly" };
            if (!validGranularities.Contains(granularity?.ToLowerInvariant()))
            {
                return NewResponse(400, ApiErrorMessages.System.InvalidGranularity(0), null);
            }

            var report = await _receiptService.GetRevenueReportAsync(cafeId, startDate, endDate, granularity!);
            return NewResponse(200, ApiSuccessMessages.Cafe.RevenueReportRetrieved, report);
        }
    }
}