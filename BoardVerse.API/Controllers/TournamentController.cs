using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// API Tournament dành cho mobile app (Player).
/// - Lấy danh sách giải Splendor đang mở.
/// - Player đăng ký / rút lui.
/// - Xem chi tiết giải + participants + matches.
/// </summary>
[ApiController]
[Route("api/v1/tournaments")]
[Authorize]
public class TournamentController : BaseApiController
{
    private readonly ITournamentService _tournamentService;

    public TournamentController(ITournamentService tournamentService)
    {
        _tournamentService = tournamentService;
    }

    /// <summary>
    /// Lấy danh sách tất cả tournament đang mở đăng ký (mọi game). [Role: Player]
    /// </summary>
    /// <response code="200">Danh sách giải đang mở đăng ký.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("open")]
    public async Task<IActionResult> GetOpenTournaments()
    {
        var userId = GetUserIdFromClaims();
        var result = await _tournamentService.GetOpenTournamentsAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.ListRetrieved, result);
    }

    /// <summary>
    /// Lấy thông tin chi tiết một giải đấu. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Chi tiết giải đấu.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{tournamentId:guid}")]
    public async Task<IActionResult> GetTournament(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _tournamentService.GetTournamentAsync(tournamentId, userId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.Retrieved, result);
    }

    /// <summary>
    /// Lấy danh sách người chơi đã đăng ký trong giải. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Danh sách người chơi.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{tournamentId:guid}/participants")]
    public async Task<IActionResult> GetParticipants(Guid tournamentId)
    {
        var result = await _tournamentService.GetParticipantsAsync(tournamentId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.ParticipantsRetrieved, result);
    }

    /// <summary>
    /// Lấy danh sách các bàn đấu của giải. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Danh sách bàn đấu (đã sắp xếp theo vòng và số bàn).</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{tournamentId:guid}/matches")]
    public async Task<IActionResult> GetMatches(Guid tournamentId)
    {
        var result = await _tournamentService.GetMatchesAsync(tournamentId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.MatchesRetrieved, result);
    }

    /// <summary>
    /// Lấy danh sách bàn đấu theo vòng. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="roundNumber">Số vòng (1-3 cho Swiss, 4 cho Final).</param>
    /// <response code="200">Danh sách bàn đấu của vòng.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{tournamentId:guid}/matches/round/{roundNumber:int}")]
    public async Task<IActionResult> GetRoundMatches(Guid tournamentId, int roundNumber)
    {
        var result = await _tournamentService.GetRoundMatchesAsync(tournamentId, roundNumber);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.RoundMatchesRetrieved, result);
    }

    /// <summary>
    /// Đăng ký tham gia giải đấu. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="201">Đăng ký thành công.</response>
    /// <response code="400">Giải không mở đăng ký hoặc đã quá hạn.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Karma không đạt yêu cầu.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Đã đăng ký rồi hoặc giải đã đủ người.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/register")]
    public async Task<IActionResult> Register(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _tournamentService.RegisterAsync(tournamentId, userId);
        return this.NewResponse(201, ApiSuccessMessages.Tournament.Registered, result);
    }

    /// <summary>
    /// Hủy đăng ký giải đấu. [Role: Player]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Hủy đăng ký thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Chưa đăng ký giải này.</response>
    /// <response code="409">Đã rút lui hoặc giải đã bắt đầu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/unregister")]
    public async Task<IActionResult> Unregister(Guid tournamentId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _tournamentService.WithdrawRegistrationAsync(tournamentId, userId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.Unregistered, result);
    }

    /// <summary>
    /// Lấy danh sách tournament mà user hiện tại đã/đang đăng ký. [Role: Player]
    /// </summary>
    /// <param name="status">Lọc theo trạng thái TournamentStatus (Draft/RegistrationOpen/RegistrationClosed/OnGoing/Completed/Cancelled).</param>
    /// <response code="200">Danh sách tournament của user (kèm status, rank, elo của user).</response>
    /// <response code="400">Status không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("my-registrations")]
    public async Task<IActionResult> GetMyRegistrations([FromQuery] string? status = null)
    {
        var userId = GetUserIdFromClaims();
        var result = await _tournamentService.GetMyRegistrationsAsync(userId, status);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.MyRegistrationsRetrieved, result);
    }

    /// <summary>
    /// Lấy lịch sử Elo của user hiện tại qua các tournament đã/đang tham gia. [Role: Player]
    /// </summary>
    /// <response code="200">Lịch sử Elo.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="404">Không tìm thấy user profile.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("my-elo-history")]
    public async Task<IActionResult> GetMyEloHistory()
    {
        var userId = GetUserIdFromClaims();
        var result = await _tournamentService.GetEloHistoryAsync(userId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.EloHistoryRetrieved, result);
    }

    /// <summary>
    /// Lấy leaderboard top players theo GlobalElo (BR-10: chỉ dùng cho Tournament). [Role: Player]
    /// </summary>
    /// <param name="topCount">Số lượng người chơi top (mặc định 100, max 500).</param>
    /// <param name="gameTemplateId">Optional: filter TournamentsPlayed/Champions count theo game (vd: chỉ Splendor).</param>
    /// <response code="200">Bảng xếp hạng Elo.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] int topCount = 100,
        [FromQuery] Guid? gameTemplateId = null)
    {
        var result = await _tournamentService.GetLeaderboardAsync(topCount, gameTemplateId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.LeaderboardRetrieved, result);
    }
}