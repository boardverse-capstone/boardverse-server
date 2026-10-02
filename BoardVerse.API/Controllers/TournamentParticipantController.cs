using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Xem thông tin chi tiết participant trong tournament (matches, stats, tiebreaker).
/// Public endpoint — ai cũng có thể xem participant detail của tournament công khai.
/// </summary>
[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/participants")]
public class TournamentParticipantController : BaseApiController
{
    private readonly ITournamentService _tournamentService;

    public TournamentParticipantController(ITournamentService tournamentService)
    {
        _tournamentService = tournamentService;
    }

    /// <summary>
    /// Xem chi tiết 1 participant (điểm, thẻ, gems, nobles, win/draw/loss). [Role: Public]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="participantId">Mã participant.</param>
    /// <response code="200">Trả về thông tin chi tiết participant.</response>
    /// <response code="404">Không tìm thấy giải hoặc participant.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{participantId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetParticipantDetail(Guid tournamentId, Guid participantId)
    {
        var result = await _tournamentService.GetParticipantDetailAsync(tournamentId, participantId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.ParticipantDetailRetrieved, result);
    }
}
