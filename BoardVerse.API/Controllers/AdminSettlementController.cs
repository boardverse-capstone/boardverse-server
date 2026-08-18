using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin endpoints cho Settlement.
/// W-06: Manual settlement override + list endpoints (mọi status / Failed).
/// </summary>
[ApiController]
[Route("api/v1/admin/settlements")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
[Tags("Admin - Settlement")]
public class AdminSettlementController : BaseApiController
{
    private readonly ISettlementService _settlementService;

    public AdminSettlementController(ISettlementService settlementService)
    {
        _settlementService = settlementService;
    }

    /// <summary>
    /// W-06: Lấy danh sách settlement có phân trang và filter (status, cafeId, cafeManagerId, khoảng ngày).
    /// Dùng khi admin muốn xem tổng quan mọi trạng thái hoặc filter status cụ thể (Pending/Retrying/Succeeded/Overridden).
    /// [Role: Admin]
    /// </summary>
    /// <param name="status">Filter theo trạng thái (Pending, Succeeded, Failed, Retrying, Overridden). Optional.</param>
    /// <param name="cafeId">Filter theo cafe. Optional.</param>
    /// <param name="cafeManagerId">Filter theo cafe manager. Optional.</param>
    /// <param name="fromUtc">Mốc bắt đầu (CreatedAt). Optional.</param>
    /// <param name="toUtc">Mốc kết thúc (CreatedAt). Optional.</param>
    /// <param name="pageNumber">Trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 20, max 100).</param>
    /// <response code="200">Danh sách settlement phân trang, sắp xếp theo UpdatedAt DESC.</response>
    /// <response code="400">Tham số không hợp lệ (vd: status không phải enum hợp lệ).</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<SettlementListItemDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetSettlements(
        [FromQuery] string? status = null,
        [FromQuery] Guid? cafeId = null,
        [FromQuery] Guid? cafeManagerId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        CafeSettlementStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!System.Enum.TryParse<CafeSettlementStatus>(status, true, out var parsed))
            {
                throw new BadRequestException(
                    ApiErrorMessages.Controller.InvalidQueryParameter(
                        "status",
                        string.Join(", ", Enum.GetNames<CafeSettlementStatus>())));
            }
            statusFilter = parsed;
        }

        var query = new SettlementListQuery
        {
            Status = statusFilter,
            CafeId = cafeId,
            CafeManagerId = cafeManagerId,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var result = await _settlementService.GetPagedAsync(query);
        return NewResponse(200, ApiSuccessMessages.Settlement.ListRetrieved, result);
    }

    /// <summary>
    /// W-06: Lấy danh sách settlement bị lỗi (Status=Failed) — endpoint chính cho admin
    /// tìm SettlementId để retry (qua AdminJobs) hoặc override sau khi retry exhaustion.
    /// Trả về đầy đủ SettlementId + CafeName + Amount + FailureReason để admin xác nhận
    /// đúng settlement (không nhầm với reservationId/sessionId).
    /// [Role: Admin]
    /// </summary>
    /// <param name="cafeId">Filter theo cafe. Optional.</param>
    /// <param name="cafeManagerId">Filter theo cafe manager. Optional.</param>
    /// <param name="fromUtc">Mốc bắt đầu (CreatedAt). Optional.</param>
    /// <param name="toUtc">Mốc kết thúc (CreatedAt). Optional.</param>
    /// <param name="pageNumber">Trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 20, max 100).</param>
    /// <response code="200">Danh sách settlement Failed phân trang, sắp xếp theo UpdatedAt DESC.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không có quyền Admin.</response>
    /// <response code="500">L�i hệ thống không mong đợi.</response>
    [HttpGet("failed")]
    [ProducesResponseType(typeof(PaginatedResponse<SettlementListItemDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetFailedSettlements(
        [FromQuery] Guid? cafeId = null,
        [FromQuery] Guid? cafeManagerId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new SettlementListQuery
        {
            Status = CafeSettlementStatus.Failed,
            CafeId = cafeId,
            CafeManagerId = cafeManagerId,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var result = await _settlementService.GetPagedAsync(query);
        return NewResponse(200, ApiSuccessMessages.Settlement.FailedRetrieved, result);
    }

    /// <summary>
    /// W-06: Admin manually override a failed settlement after retry exhaustion.
    /// Sets Status = Overridden, OverrideBy = adminId, OverrideAt = now.
    /// [Role: Admin]
    /// </summary>
    /// <param name="settlementId">Mã settlement cần override.</param>
    /// <response code="200">Override thành công.</response>
    /// <response code="404">Không tìm thấy settlement.</response>
    /// <response code="409">Settlement đã được override trước đó.</response>
    [HttpPost("{settlementId:guid}/override")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> OverrideSettlement(Guid settlementId)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _settlementService.OverrideSettlementAsync(settlementId, adminUserId);
        return NewResponse(200, $"Settlement '{settlementId}' đã được override bởi admin.", new
        {
            result.Id,
            result.Status,
            result.OverrideBy,
            result.OverrideAt
        });
    }
}
