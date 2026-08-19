using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// T-03: API Tournament Waitlist - quản lý danh sách chờ khi tournament đầy.
/// </summary>
[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/waitlist")]
[Authorize]
public class TournamentWaitlistController : BaseApiController
{
    private readonly ITournamentWaitlistService _waitlistService;

    public TournamentWaitlistController(ITournamentWaitlistService waitlistService)
    {
        _waitlistService = waitlistService;
    }

    /// <summary>
    /// T-03: Tham gia waitlist của một tournament đầy. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Tham gia waitlist thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Đã đăng ký hoặc đã trong waitlist.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost]
    public async Task<IActionResult> JoinWaitlist(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _waitlistService.JoinWaitlistAsync(userId, tournamentId);
        return this.NewResponse(200, "Đã tham gia waitlist.", result);
    }

    /// <summary>
    /// T-03: Lấy danh sách waitlist của một tournament. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Danh sách waitlist.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet]
    public async Task<IActionResult> GetWaitlist(Guid tournamentId)
    {
        var result = await _waitlistService.GetWaitlistAsync(tournamentId);
        return this.NewResponse(200, "Danh sách waitlist.", result);
    }

    /// <summary>
    /// T-03: Lấy thông tin waitlist của user hiện tại. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Thông tin waitlist entry của user.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyWaitlistEntry(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _waitlistService.GetMyWaitlistEntryAsync(userId, tournamentId);
        return this.NewResponse(200, "Thông tin waitlist của bạn.", result);
    }

    /// <summary>
    /// T-03: Hủy tham gia waitlist. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Hủy waitlist thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Bạn không có trong waitlist.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete]
    public async Task<IActionResult> CancelWaitlist(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        await _waitlistService.CancelWaitlistAsync(userId, tournamentId);
        return this.NewResponse(200, "Đã hủy khỏi waitlist.", (object?)null);
    }

    /// <summary>
    /// T-03: Xác nhận tham gia tournament từ waitlist (khi có offer). [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Xác nhận thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Bạn không có trong waitlist.</response>
    /// <response code="409">Offer đã hết hạn hoặc không có offer.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmFromWaitlist(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _waitlistService.ConfirmFromWaitlistAsync(userId, tournamentId);
        return this.NewResponse(200, "Đã xác nhận tham gia tournament.", result);
    }

    /// <summary>
    /// T-03: Từ chối offer từ waitlist. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Từ chối thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Bạn không có trong waitlist.</response>
    /// <response code="409">Bạn không có offer nào để từ chối.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("decline")]
    public async Task<IActionResult> DeclineOffer(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _waitlistService.DeclineOfferAsync(userId, tournamentId);
        return this.NewResponse(200, "Đã từ chối offer.", result);
    }
}
