using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// **FLOW A — RESERVATION (MỚI, BVC wallet)**. KHÔNG dùng IBookingRepository.
/// API Reservation flow mới — Phase 2/3 (BR §21A.2..21A.6, §XXI-B.1).
/// Theo rule `lobby-booking-deposit-bvc.mdc`:
/// - POST /quote            : validate eligibility + tính cọc (idempotent, KHÔNG tạo row).
/// - POST /confirm          : atomic transaction hold BVC + seat + game + tạo Reservation + Lobby.
/// - POST /{id}/cancel      : host hủy theo BR-REFUND-02/03.
/// - POST /{id}/cafe-approval: cafe duyệt lobby pending (BR-NEW-11).
///
/// Endpoint <c>POST /api/v1/lobbies</c> (LobbyController.CreateLobby) cũ đã bị chặn —
/// lobby chỉ được tạo thông qua reservation flow.
/// </summary>
[ApiController]
[Route("api/v1/reservations")]
[Authorize]
public class ReservationController : BaseApiController
{
    private readonly IReservationService _reservationService;
    private readonly IReservationExtensionService _extensionService;

    public ReservationController(
        IReservationService reservationService,
        IReservationExtensionService extensionService)
    {
        _reservationService = reservationService;
        _extensionService = extensionService;
    }

    /// <summary>
    /// Kiểm tra message có phải là user limit error (403) hay conflict error (409).
    /// </summary>
    private static int GetStatusCodeForError(string message)
    {
        // BR-USER-LIMIT: account status, cooling-off, cross-role → 403
        // BR-USER-LIMIT: overlap, already has lobby, cap exceeded → 409
        var userLimit403 = new[]
        {
            "suspended",
            "banned",
            "bị giới hạn",
            "cooling-off",
            "thành viên của.*lobby",
            "host của.*lobby"
        };

        foreach (var pattern in userLimit403)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(message, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return 403;
            }
        }

        return 409;
    }

    /// <summary>
    /// Tạo quote cho reservation. [Role: Player]
    /// Quote chỉ validate + tính toán, KHÔNG tạo row DB. Idempotent theo IdempotencyKey.
    /// </summary>
    /// <param name="request">CafeId, GameId, PlayDate, MinPlayers, MaxPlayers, PreferredStartTime, PreferredEndTime, IdempotencyKey.</param>
    /// <response code="200">Trả quote gồm số BVC cần hold, balance hiện tại, missing amount, buffer, expiresAt.</response>
    /// <response code="400">Request không hợp lệ (playDate ngoài [today, +7], minPlayers &lt; 1, maxPlayers &lt; 1, preferredStartTime không n�m trong cafe schedule).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">User bị suspended/banned hoặc vượt cap tổng heldBalance.</response>
    /// <response code="404">Cafe/Game không tồn tại hoặc cafe không có game này.</response>
    /// <response code="409">Đã có lobby overlap, hoặc đã host lobby cho playDate+cafe+slot này.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("quote")]
    public async Task<IActionResult> CreateQuote([FromBody] ReservationQuoteRequestDto request)
    {
        try
        {
            var userId = GetUserIdFromClaims();
            var quote = await _reservationService.CreateQuoteAsync(userId, request);
            return this.NewResponse(200, "ReservationQuoteCreated", quote);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = GetStatusCodeForError(ex.Message);
            return this.NewResponse(statusCode, ex.Message, null);
        }
    }

    /// <summary>
    /// Xác nhận reservation — atomic transaction (§21A.3 + BR-REQUIRED §17.4). [Role: Player]
    /// Trừ BVC từ ví, giữ seat + game copy, tạo Reservation + Lobby trong 1 transaction.
    /// Lobby có playDate &gt; 2 ngày sẽ vào trạng thái PendingCafeApproval (BR-NEW-11).
    /// </summary>
    /// <param name="request">CafeId, GameId, PlayDate, MinPlayers, MaxPlayers, PreferredStartTime, PreferredEndTime, ExpectedFinalDeposit, IdempotencyKey.</param>
    /// <response code="201">Reservation + Lobby đã được tạo trong transaction, trả ReservationId, LobbyId, RecruitmentDeadline, RequiresCafeApproval, CafeApprovalDeadline, HeldBvc.</response>
    /// <response code="400">Quote đã thay đổi (ExpectedFinalDeposit sai), buffer quá ngắn, insufficient balance, validate thất bại.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">User không đủ điều kiện.</response>
    /// <response code="404">Cafe/Game không tồn tại.</response>
    /// <response code="409">IdempotencyKey đã dùng cho user khác; hoặc cafe hết chỗ / hết game copy.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ReservationConfirmRequestDto request)
    {
        try
        {
            var userId = GetUserIdFromClaims();
            var result = await _reservationService.ConfirmAsync(userId, request);
            return this.NewResponse(201, "ReservationConfirmed", result);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = GetStatusCodeForError(ex.Message);
            return this.NewResponse(statusCode, ex.Message, null);
        }
    }

    /// <summary>
    /// Lấy chi tiết 1 reservation. [Role: Player — chỉ host hoặc member mới thấy]
    /// </summary>
    /// <param name="reservationId">Mã reservation.</param>
    /// <response code="200">Chi tiết reservation.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền xem.</response>
    /// <response code="404">Không tìm thấy.</response>
    [HttpGet("{reservationId:guid}")]
    public async Task<IActionResult> GetReservation(Guid reservationId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.GetByIdAsync(userId, reservationId);
        if (result == null)
        {
            return this.NewResponse(404, ApiErrorMessages.System.ReservationAccessDenied(reservationId), null);
        }
        return this.NewResponse(200, "ReservationDetailRetrieved", result);
    }

    /// <summary>
    /// Lấy danh sách reservation của user. [Role: Player]
    /// Mặc định: chỉ reservation do user host. Dùng joinedByMe=true để xem reservation đã tham gia.
    /// </summary>
    /// <param name="request">Filter theo status, playDate, cafeId; switch hostedByMe/joinedByMe.</param>
    /// <response code="200">Danh sách reservation (phân trang).</response>
    /// <response code="401">Thiếu token.</response>
    [HttpGet]
    public async Task<IActionResult> GetReservations([FromQuery] ReservationListRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.GetListAsync(userId, request);
        return this.NewResponse(200, "ReservationsRetrieved", result);
    }

    /// <summary>
    /// Tìm kiếm lịch hẹn theo tên game hoặc ngày tháng. [Role: Player]
    /// </summary>
    /// <param name="request">
    /// - gameName: từ khóa tìm kiếm theo tên game (fuzzy search).
    /// - fromDate: ngày bắt đầu filter (inclusive).
    /// - toDate: ngày kết thúc filter (inclusive).
    /// - statuses: filter theo trạng thái.
    /// - cafeId: filter theo cafe.
    /// - hostedByMe: chỉ lấy reservation do user host (default true).
    /// - joinedByMe: chỉ lấy reservation user tham gia (default false).
    /// </param>
    /// <response code="200">Danh sách reservation tìm được (phân trang).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("search")]
    public async Task<IActionResult> SearchReservations([FromQuery] ReservationSearchRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.SearchAsync(userId, request);
        return this.NewResponse(200, "ReservationsSearched", result);
    }

    /// <summary>
    /// Lấy danh sách lobby đang chờ cafe duyệt (BR-NEW-11). [Role: Cafe Manager]
    /// Lobby có playDate > 2 ngày sẽ ở trạng thái PendingCafeApproval và cần manager duyệt.
    /// </summary>
    /// <param name="request">Filter theo cafeId, playDate, lobbyStatus; phân trang.</param>
    /// <response code="200">Danh sách lobby pending approval (phân trang).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải manager của bất kỳ cafe nào.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("pending-cafe-approval")]
    // P0-Fix-#5: "Staff" không tồn tại trong UserRole enum → dùng "CafeStaff" theo BoardVerse.Core/Enum/UserRole.cs.
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> GetPendingCafeApprovals([FromQuery] LobbyPendingApprovalRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.GetPendingCafeApprovalAsync(userId, request);
        return this.NewResponse(200, "PendingCafeApprovalLobbiesRetrieved", result);
    }

    /// <summary>
    /// Lấy chi tiết một reservation đang chờ cafe duyệt (BR-NEW-11). [Role: Cafe Manager]
    /// </summary>
    /// <param name="reservationId">Id reservation cần xem chi tiết.</param>
    /// <response code="200">Chi tiết reservation pending approval.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải manager của cafe.</response>
    /// <response code="404">Không tìm thấy reservation hoặc reservation không ở trạng thái PendingCafeApproval.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("{reservationId:guid}/cafe-approval")]
    // P0-Fix-#5: "Staff" không tồn tại trong UserRole enum → dùng "CafeStaff".
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> GetPendingCafeApprovalDetail(Guid reservationId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.GetPendingCafeApprovalDetailAsync(userId, reservationId);

        if (result == null)
        {
            return this.NewResponse(404, ApiErrorMessages.Reservation.ReservationNotFound(reservationId), null);
        }

        return this.NewResponse(200, "PendingCafeApprovalDetailRetrieved", result);
    }

    /// <summary>
    /// Host hủy reservation (BR-REFUND-02/03). [Role: Player — chỉ host của reservation mới được hủy]
    /// Áp dụng policy: grace 15p chưa có member → 100%; ≥24h trước giờ chơi → 100%; 6-24h → 50%; &lt;6h → 0%.
    /// </summary>
    /// <param name="reservationId">Id reservation cần hủy.</param>
    /// <param name="request">Lý do hủy (optional, tối đa 500 ký tự).</param>
    /// <response code="200">Trả RefundBvc, ForfeitBvc, RefundPolicyApplied.</response>
    /// <response code="400">Reservation không ở trạng thái Holding.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải host của reservation.</response>
    /// <response code="404">Không tìm thấy reservation.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{reservationId:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid reservationId,
        [FromBody] CancelReservationRequestDto request)
    {
        try
        {
            // Override request.ReservationId bằng route để tránh body sai id.
            request.ReservationId = reservationId;
            var userId = GetUserIdFromClaims();
            var result = await _reservationService.CancelAsync(userId, request);
            return this.NewResponse(200, "ReservationCancelled", result);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = GetStatusCodeForError(ex.Message);
            return this.NewResponse(statusCode, ex.Message, null);
        }
    }

    /// <summary>
    /// BR-REFUND-08 (walk-in-override-design §2.3):
    /// Host hủy reservation SAU khi đã check-in tại quán (late cancel).
    /// Áp dụng soft-release refund 30% nếu playedRatio ≥ 50% slot, forfeit toàn bộ nếu &lt; 50%.
    /// Khác <see cref="Cancel"/>: chỉ áp dụng cho Reservation đã check-in (status = CheckedIn).
    /// Cancel trước check-in → dùng <c>POST /api/v1/reservations/{id}/cancel</c> (BR-REFUND-02/03).
    /// [Role: Player (host của reservation)]
    /// </summary>
    /// <param name="reservationId">Id reservation đã check-in.</param>
    /// <param name="request">Optional reason.</param>
    /// <response code="200">Trả CancelAfterCheckinResponseDto với refund/forfeit breakdown.</response>
    /// <response code="400">Reservation chưa check-in (status ≠ CheckedIn).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải host của reservation.</response>
    /// <response code="404">Không tìm thấy reservation.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{reservationId:guid}/cancel-after-checkin")]
    public async Task<IActionResult> CancelAfterCheckin(
        Guid reservationId,
        [FromBody] CancelAfterCheckinRequestDto request)
    {
        request.ReservationId = reservationId;
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.CancelAfterCheckinAsync(userId, request);
        return this.NewResponse(200, "CancelAfterCheckinSuccess", result);
    }

    /// <summary>
    /// Cafe duyệt hoặc từ chối lobby đang chờ (BR-NEW-11). [Role: Cafe Manager]
    /// Sau khi approve → lobby chuyển sang Open, public cho members join.
    /// Sau khi reject → lobby chuyển RejectedByCafe, refund 100% BVC cho host.
    /// </summary>
    /// <param name="reservationId">Id reservation cần duyệt.</param>
    /// <param name="request">Approve=true/false, Reason (optional).</param>
    /// <response code="200">Trả Approved, LobbyStatus, RefundBvc.</response>
    /// <response code="400">Lobby không ở trạng thái PendingCafeApproval.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải manager của cafe.</response>
    /// <response code="404">Không tìm thấy reservation.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{reservationId:guid}/cafe-approval")]
    // P0-Fix-#5: "Staff" không tồn tại trong UserRole enum → dùng "CafeStaff".
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> CafeApproval(
        Guid reservationId,
        [FromBody] CafeApprovalRequestDto request)
    {
        try
        {
            request.ReservationId = reservationId;
            var userId = GetUserIdFromClaims();
            var result = await _reservationService.HandleCafeApprovalAsync(userId, request);
            return this.NewResponse(200,
                request.Approve ? "ReservationCafeApproved" : "ReservationCafeRejected",
                result);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = GetStatusCodeForError(ex.Message);
            return this.NewResponse(statusCode, ex.Message, null);
        }
    }

    /// <summary>
    /// Kiểm tra xem có thể extend reservation không (BR-EXT). [Role: Host]
    /// </summary>
    /// <param name="reservationId">Id reservation cần extend.</param>
    /// <param name="extensionMinutes">Số phút muốn extend.</param>
    /// <response code="200">Trả thông tin availability.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="404">Không tìm thấy reservation.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpGet("{reservationId:guid}/extend/availability")]
    public async Task<IActionResult> CheckExtendAvailability(
        Guid reservationId,
        [FromQuery] int extensionMinutes)
    {
        try
        {
            var result = await _extensionService.CheckAvailabilityAsync(reservationId, extensionMinutes);
            return this.NewResponse(200, "ExtendAvailability", result);
        }
        catch (NotFoundException ex)
        {
            return this.NewResponse(404, ex.Message, null);
        }
    }

    /// <summary>
    /// Extend thời gian reservation (BR-EXT). [Role: Host]
    ///
    /// BR-EXT-01: Chỉ extend khi Status = Confirmed.
    /// BR-EXT-02: Không extend qua midnight.
    /// BR-EXT-03: Max 2 lần (tổng max 120 phút).
    /// BR-EXT-05: Partial extension OK.
    /// </summary>
    /// <param name="reservationId">Id reservation cần extend.</param>
    /// <param name="request">Thông tin extend (số phút).</param>
    /// <response code="200">Extend thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải host của reservation.</response>
    /// <response code="404">Không tìm thấy reservation.</response>
    /// <response code="409">Conflict: đã extend tối đa, overlap, hoặc status không cho phép.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{reservationId:guid}/extend")]
    public async Task<IActionResult> Extend(
        Guid reservationId,
        [FromBody] ExtendReservationRequestDto request)
    {
        try
        {
            request.ReservationId = reservationId;
            var userId = GetUserIdFromClaims();
            var result = await _extensionService.ExtendAsync(request, userId);
            return this.NewResponse(200, "ReservationExtended", result);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = GetStatusCodeForError(ex.Message);
            return this.NewResponse(statusCode, ex.Message, null);
        }
        catch (NotFoundException ex)
        {
            return this.NewResponse(404, ex.Message, null);
        }
    }

    /// <summary>
    /// BR §21A.7: POS scan QR check-in reservation. [Role: Cafe Staff]
    /// Atomic transition Reservation.Status = Confirmed → CheckedIn, Lobby.Status = InProgress.
    /// Idempotent theo ReservationCode (gọi 2 lần cùng code → trả kết quả cũ).
    /// Ghi Reservation.CheckedInAt để Phase 7 karma system tính playedRatio.
    /// </summary>
    /// <param name="reservationId">Id reservation.</param>
    /// <param name="request">ReservationCode (8-char alphanumeric), optional note.</param>
    /// <response code="200">Check-in thành công.</response>
    /// <response code="400">ReservationCode không khớp hoặc status không cho phép.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải staff của cafe.</response>
    /// <response code="404">Không tìm thấy reservation.</response>
    /// <response code="409">Reservation chưa được confirm / đã check-in rồi.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{reservationId:guid}/check-in")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> CheckIn(
        Guid reservationId,
        [FromBody] ReservationCheckInRequestDto request)
    {
        try
        {
            // Reservation.id lấy từ route, không từ body.
            request.CafeId = request.CafeId; // giữ nguyên
            var userId = GetUserIdFromClaims();
            var result = await _reservationService.CheckInAsync(userId, new ReservationCheckInRequestDto
            {
                CafeId = request.CafeId,
                ReservationCode = request.ReservationCode,
                ActiveSessionId = request.ActiveSessionId,
                IdempotencyKey = request.IdempotencyKey
            });
            return this.NewResponse(200, "ReservationCheckedIn", result);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = GetStatusCodeForError(ex.Message);
            return this.NewResponse(statusCode, ex.Message, null);
        }
    }

    /// <summary>
    /// BR §21A.7: POS scan QR check-in theo ReservationCode. [Role: Cafe Staff]
    /// Endpoint thay thế cho FE không biết reservationId, chỉ cần ReservationCode từ QR.
    /// Atomic transition Reservation.Status = Confirmed → CheckedIn, Lobby.Status = InProgress.
    /// Idempotent theo ReservationCode.
    /// </summary>
    /// <param name="reservationCode">Mã 8-char alphanumeric từ QR code.</param>
    /// <param name="request">CafeId, ActiveSessionId, TableNumber, IdempotencyKey.</param>
    /// <response code="200">Check-in thành công.</response>
    /// <response code="400">Request không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải staff của cafe.</response>
    /// <response code="404">Không tìm thấy reservation.</response>
    /// <response code="409">Reservation không thuộc cafe hoặc đã check-in rồi.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("by-code/{reservationCode}/check-in")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> CheckInByCode(
        string reservationCode,
        [FromBody] CheckInByCodeRequestDto request)
    {
        try
        {
            var userId = GetUserIdFromClaims();
            var result = await _reservationService.CheckInByCodeAsync(userId, reservationCode, request);
            return this.NewResponse(200, "ReservationCheckedIn", result);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = GetStatusCodeForError(ex.Message);
            return this.NewResponse(statusCode, ex.Message, null);
        }
    }

    /// <summary>
    /// BR-END-01..05 (§21A.8, §3.4): POS kết thúc session + settle deposit. [Role: Cafe Staff]
    ///
    /// Tính playedRatio = (ActualEndAt - CheckedInAt) / (ScheduledEndTime - ScheduledStartTime).
    /// Áp dụng refund policy:
    /// - playedRatio ≥ 90% → OnTime, capture 100% BVC về doanh thu quán (no refund).
    /// - playedRatio 50-90% → EarlyCheckout, refund 30% BVC cho host, forfeit 70%.
    /// - playedRatio &lt; 50% → EarlyCheckout, forfeit 100% BVC.
    ///
    /// Transition: Reservation.Status CheckedIn → Completed/EarlyCheckout.
    /// Nếu playedRatio &lt; 50% tạo WalkInWindow cho phần thời gian còn lại (EC-09).
    /// </summary>
    /// <param name="reservationId">Id reservation.</param>
    /// <param name="request">ActualEndAt (optional, default = now), Reason (optional).</param>
    /// <response code="200">Trả RefundBvc, ForfeitBvc, EndReason, PlayedRatio, WalkInWindowId.</response>
    /// <response code="400">Reservation chưa check-in hoặc session invalid.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải staff của cafe.</response>
    /// <response code="404">Không tìm thấy reservation.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{reservationId:guid}/end")]
    [Authorize(Roles = "Manager,CafeStaff")]
    public async Task<IActionResult> End(
        Guid reservationId,
        [FromBody] EndReservationRequestDto request)
    {
        try
        {
            request.ReservationId = reservationId;
            var userId = GetUserIdFromClaims();
            var result = await _reservationService.EndAndSettleAsync(userId, request);
            return this.NewResponse(200, "ReservationEnded", result);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = GetStatusCodeForError(ex.Message);
            return this.NewResponse(statusCode, ex.Message, null);
        }
    }
}
