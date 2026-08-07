using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin endpoints cho Tournament: CRUD đầy đủ, force-complete, view participants.
/// [Role: Admin]
/// </summary>
[ApiController]
[Route("api/v1/admin/tournaments")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
[Tags("Admin - Tournament")]
public class AdminTournamentController : BaseApiController
{
    private readonly ITournamentService _tournamentService;

    public AdminTournamentController(ITournamentService tournamentService)
    {
        _tournamentService = tournamentService;
    }

    /// <summary>
    /// Lấy danh sách tất cả tournaments (phân trang, filter theo status/cancel).
    /// [Role: Admin]
    /// </summary>
    /// <param name="page">Số trang (mặc định 1).</param>
    /// <param name="pageSize">Kích thước trang (mặc định 20, max 100).</param>
    /// <param name="status">Filter theo TournamentStatus (Draft, RegistrationOpen, RegistrationClosed, OnGoing, Completed, Cancelled).</param>
    /// <param name="cafeId">Filter theo cafe (optional).</param>
    /// <response code="200">Danh sách tournaments phân trang.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet]
    [ProducesResponseType(typeof(AdminTournamentListResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetTournaments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? cafeId = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var result = await _tournamentService.GetAdminTournamentsAsync(page, pageSize, searchTerm, status, cafeId);
        return NewResponse(200, ApiSuccessMessages.Tournament.ListRetrieved, result);
    }

    /// <summary>
    /// Lấy chi tiết một tournament.
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Chi tiết giải đấu.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{tournamentId:guid}")]
    [ProducesResponseType(typeof(AdminTournamentDetailDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetTournament(Guid tournamentId)
    {
        var result = await _tournamentService.GetAdminTournamentDetailAsync(tournamentId);
        if (result == null)
        {
            throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));
        }
        return NewResponse(200, ApiSuccessMessages.Tournament.Retrieved, result);
    }

    /// <summary>
    /// Tạo tournament mới (Admin tạo thay cho manager).
    /// [Role: Admin]
    /// </summary>
    /// <param name="request">Thông tin tournament cần tạo.</param>
    /// <response code="201">Tournament đã được tạo.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost]
    [ProducesResponseType(typeof(TournamentResponseDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> CreateTournament([FromBody] AdminCreateTournamentRequestDto request)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _tournamentService.AdminCreateTournamentAsync(adminUserId, request);
        return NewResponse(201, ApiSuccessMessages.Tournament.Created, result);
    }

    /// <summary>
    /// Cập nhật tournament (Admin sửa thay manager).
    /// Chỉ cho phép khi tournament ở trạng thái Draft.
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="request">Thông tin cần cập nhật.</param>
    /// <response code="200">Tournament đã được cập nhật.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Tournament không ở trạng thái Draft.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPut("{tournamentId:guid}")]
    [ProducesResponseType(typeof(TournamentResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> UpdateTournament(
        Guid tournamentId,
        [FromBody] AdminUpdateTournamentRequestDto request)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _tournamentService.AdminUpdateTournamentAsync(adminUserId, tournamentId, request);
        return NewResponse(200, ApiSuccessMessages.Tournament.Updated, result);
    }

    /// <summary>
    /// Xóa tournament (Admin xóa thay manager).
    /// Chỉ cho phép khi tournament ở trạng thái Draft.
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Tournament đã được xóa.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Tournament không ở trạng thái Draft.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete("{tournamentId:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> DeleteTournament(Guid tournamentId)
    {
        var adminUserId = GetUserIdFromClaims();
        await _tournamentService.AdminDeleteTournamentAsync(adminUserId, tournamentId);
        return NewResponse(200, "Tournament đã được xóa.", new { id = tournamentId });
    }

    /// <summary>
    /// Lấy danh sách participants của tournament.
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="status">Filter theo participant status (Registered, CheckedIn, Active, Finished, Withdrawn, NoShow).</param>
    /// <response code="200">Danh sách participants.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{tournamentId:guid}/participants")]
    [ProducesResponseType(typeof(AdminTournamentParticipantsResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetParticipants(
        Guid tournamentId,
        [FromQuery] string? status = null)
    {
        var result = await _tournamentService.GetAdminTournamentParticipantsAsync(tournamentId, status);
        return NewResponse(200, ApiSuccessMessages.Tournament.ParticipantsRetrieved, result);
    }

    /// <summary>
    /// Mở đăng ký tournament (Admin thay manager).
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Đã mở đăng ký.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Tournament không ở trạng thái hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/open-registration")]
    [ProducesResponseType(typeof(TournamentResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> OpenRegistration(Guid tournamentId)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _tournamentService.AdminOpenRegistrationAsync(adminUserId, tournamentId);
        return NewResponse(200, ApiSuccessMessages.Tournament.RegistrationOpened, result);
    }

    /// <summary>
    /// Đóng đăng ký tournament (Admin thay manager).
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Đã đóng đăng ký.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Tournament không ở trạng thái hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/close-registration")]
    [ProducesResponseType(typeof(TournamentResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> CloseRegistration(Guid tournamentId)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _tournamentService.AdminCloseRegistrationAsync(adminUserId, tournamentId);
        return NewResponse(200, ApiSuccessMessages.Tournament.RegistrationClosed, result);
    }

    /// <summary>
    /// Bắt đầu tournament (Admin thay manager).
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Tournament đã bắt đầu.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Không đủ người hoặc tournament không ở trạng thái hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/start")]
    [ProducesResponseType(typeof(TournamentResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> StartTournament(Guid tournamentId)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _tournamentService.AdminStartTournamentAsync(adminUserId, tournamentId);
        return NewResponse(200, ApiSuccessMessages.Tournament.Started, result);
    }

    /// <summary>
    /// Hoàn thành tournament (Admin force-complete).
    /// Chỉ cho phép khi tournament đang OnGoing.
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Tournament đã được hoàn thành.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Tournament không ở trạng thái OnGoing.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/complete")]
    [ProducesResponseType(typeof(TournamentResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> CompleteTournament(Guid tournamentId)
    {
        var adminUserId = GetUserIdFromClaims();
        var result = await _tournamentService.AdminCompleteTournamentAsync(adminUserId, tournamentId);
        return NewResponse(200, ApiSuccessMessages.Tournament.Completed, result);
    }

    /// <summary>
    /// Hủy tournament (Admin thay manager).
    /// [Role: Admin]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="request">Lý do hủy (bắt buộc nếu tournament đã có participants).</param>
    /// <response code="200">Tournament đã được hủy.</response>
    /// <response code="400">Lý do bắt buộc khi tournament đã có người đăng ký.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Tournament đã hoàn thành hoặc đã bị hủy trước đó.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/cancel")]
    [ProducesResponseType(typeof(TournamentResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> CancelTournament(
        Guid tournamentId,
        [FromBody] AdminCancelTournamentRequestDto? request = null)
    {
        var adminUserId = GetUserIdFromClaims();
        var reason = request?.Reason;
        var result = await _tournamentService.AdminCancelTournamentAsync(adminUserId, tournamentId, reason);
        return NewResponse(200, ApiSuccessMessages.Tournament.Cancelled, result);
    }
}

/// <summary>
/// Request DTO for Admin Cancel Tournament.
/// </summary>
public class AdminCancelTournamentRequestDto
{
    /// <summary>
    /// Lý do hủy tournament (bắt buộc khi tournament đã có participants).
    /// </summary>
    public string? Reason { get; set; }
}
