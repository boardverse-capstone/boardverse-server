using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin endpoints cho Reservation.
/// BR-REFUND-07: Admin override refund amount.
/// </summary>
[ApiController]
[Route("api/v1/admin/reservations")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
[Tags("Admin - Reservation")]
public class AdminReservationController : BaseApiController
{
    private readonly IReservationService _reservationService;

    public AdminReservationController(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    /// <summary>
    /// Admin override refund amount cho reservation đã completed (BR-REFUND-07).
    /// Cho phép refund một phần hoặc toàn bộ số BVC đã capture.
    /// Ghi AdminCredit ledger entry + PlayerActionHistory audit (BR-RISK-05).
    /// Idempotent theo Idempotency-Key header.
    /// [Role: Admin]
    /// </summary>
    /// <param name="reservationId">ReservationId cần override refund.</param>
    /// <param name="request">Số BVC refund + lý do.</param>
    /// <param name="idempotencyKey">Header Idempotency-Key, dùng để chống trùng (BR § XVII.1).</param>
    /// <response code="200">Override thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (refund amount &gt; deposit, reason &lt; 5 ký tự).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Reservation không tìm thấy.</response>
    /// <response code="409">Reservation không ở trạng thái Completed.</response>
    /// <response code="500">Lỗi hệ thống.</response>
    [HttpPost("{reservationId:guid}/override-refund")]
    [ProducesResponseType(typeof(AdminOverrideRefundResultDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(object), 404)]
    [ProducesResponseType(typeof(object), 409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> OverrideRefund(
        [FromRoute] Guid reservationId,
        [FromBody] AdminOverrideRefundRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return NewResponse(400, ApiErrorMessages.Wallet.IdempotencyKeyRequired, null);
        }

        try
        {
            var adminUserId = GetUserIdFromClaims();
            var result = await _reservationService.AdminOverrideRefundAsync(
                adminUserId,
                reservationId,
                request,
                idempotencyKey);

            return NewResponse(200,
                $"Đã override refund {result.ActualRefundAmount} BVC cho reservation '{reservationId}'.",
                result);
        }
        catch (NotFoundException ex)
        {
            return NewResponse(404, ex.Message, null);
        }
        catch (ConflictException ex)
        {
            return NewResponse(409, ex.Message, null);
        }
        catch (BadRequestException ex)
        {
            return NewResponse(400, ex.Message, null);
        }
    }
}
