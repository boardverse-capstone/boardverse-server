using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// T-04: API Tournament Spectator - cho phép user spectate (theo dõi) tournament mà không cần đăng ký làm participant.
/// </summary>
[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/spectators")]
[Authorize]
public class TournamentSpectatorController : BaseApiController
{
    private readonly ITournamentSpectatorService _spectatorService;

    public TournamentSpectatorController(ITournamentSpectatorService spectatorService)
    {
        _spectatorService = spectatorService;
    }

    /// <summary>
    /// T-04: Bắt đầu spectate một tournament. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu muốn spectate.</param>
    /// <response code="200">Spectate thành công, trả thông tin spectate entry.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">User là participant của tournament này.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost]
    [ProducesResponseType(typeof(TournamentSpectatorDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> Spectate(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _spectatorService.SpectateAsync(userId, tournamentId);
        return NewResponse(200, ApiSuccessMessages.Tournament.Registered, result);
    }

    /// <summary>
    /// T-04: Rời khỏi spectate. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Rời spectate thành công.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="404">Không tìm thấy spectate entry.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> LeaveSpectate(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        await _spectatorService.LeaveSpectateAsync(userId, tournamentId);
        return NewResponse(200, "Đã rời khỏi spectate.", (object?)null);
    }

    /// <summary>
    /// T-04: Lấy spectate entry của user hiện tại cho một tournament. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Thông tin spectate entry hoặc null nếu chưa spectate.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(TournamentSpectatorDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetMySpectateEntry(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _spectatorService.GetMySpectatorEntryAsync(userId, tournamentId);
        return NewResponse(200, "Thông tin spectate của bạn.", result);
    }

    /// <summary>
    /// T-04: Lấy danh sách spectators của một tournament (public). [Role: Public]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Danh sách spectators.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<TournamentSpectatorDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetSpectators(Guid tournamentId)
    {
        var result = await _spectatorService.GetSpectatorsAsync(tournamentId);
        return NewResponse(200, "Danh sách spectators.", result);
    }
}
