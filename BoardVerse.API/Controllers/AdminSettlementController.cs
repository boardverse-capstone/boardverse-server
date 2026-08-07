using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin endpoints cho Settlement.
/// W-06: Manual settlement override endpoint.
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
