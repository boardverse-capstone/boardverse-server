using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
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

    public ReservationController(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    /// <summary>
    /// Tạo quote cho reservation. [Role: Player]
    /// Quote chỉ validate + tính toán, KHÔNG tạo row DB. Idempotent theo IdempotencyKey.
    /// </summary>
    /// <param name="request">CafeId, GameId, PlayDate, TimeSlot, MinPlayers, MaxPlayers, PreferredStartTime, IdempotencyKey.</param>
    /// <response code="200">Trả quote gồm số BVC cần hold, balance hiện tại, missing amount, buffer, expiresAt.</response>
    /// <response code="400">Request không hợp lệ (playDate ngoài [today, +7], minPlayers &lt; 2, timeSlot không đúng, preferredStartTime không nằm trong timeSlot window).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">User bị suspended/banned hoặc vượt cap tổng heldBalance.</response>
    /// <response code="404">Cafe/Game không tồn tại hoặc cafe không có game này.</response>
    /// <response code="409">Đã có lobby overlap, hoặc đã host lobby cho playDate+cafe+slot này.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("quote")]
    public async Task<IActionResult> CreateQuote([FromBody] ReservationQuoteRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var quote = await _reservationService.CreateQuoteAsync(userId, request);
        return this.NewResponse(200, "ReservationQuoteCreated", quote);
    }

    /// <summary>
    /// Xác nhận reservation — atomic transaction (§21A.3 + BR-REQUIRED §17.4). [Role: Player]
    /// Trừ BVC từ ví, giữ seat + game copy, tạo Reservation + Lobby trong 1 transaction.
    /// Lobby có playDate &gt; 2 ngày sẽ vào trạng thái PendingCafeApproval (BR-NEW-11).
    /// </summary>
    /// <param name="request">CafeId, GameId, PlayDate, TimeSlot, MinPlayers, MaxPlayers, ExpectedFinalDeposit, IdempotencyKey.</param>
    /// <response code="200">Trả ReservationId, LobbyId, RecruitmentDeadline, RequiresCafeApproval, CafeApprovalDeadline, HeldBvc.</response>
    /// <response code="400">Quote đã thay đổi (ExpectedFinalDeposit sai), buffer quá ngắn, insufficient balance, validate thất bại.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">User không đủ điều kiện.</response>
    /// <response code="404">Cafe/Game không tồn tại.</response>
    /// <response code="409">IdempotencyKey đã dùng cho user khác; hoặc cafe hết chỗ / hết game copy.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ReservationConfirmRequestDto request)
    {
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.ConfirmAsync(userId, request);
        return this.NewResponse(200, "ReservationConfirmed", result);
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
        // Override request.ReservationId bằng route để tránh body sai id.
        request.ReservationId = reservationId;
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.CancelAsync(userId, request);
        return this.NewResponse(200, "ReservationCancelled", result);
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
    public async Task<IActionResult> CafeApproval(
        Guid reservationId,
        [FromBody] CafeApprovalRequestDto request)
    {
        request.ReservationId = reservationId;
        var userId = GetUserIdFromClaims();
        var result = await _reservationService.HandleCafeApprovalAsync(userId, request);
        return this.NewResponse(200,
            request.Approve ? "ReservationCafeApproved" : "ReservationCafeRejected",
            result);
    }
}
