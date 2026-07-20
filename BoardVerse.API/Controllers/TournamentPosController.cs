using System.Security.Claims;
using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// API Tournament dành cho Web POS (Manager).
/// - Tạo / sửa / mở đăng ký / bắt đầu / hủy / hoàn thành giải.
/// - Check-in người chơi tại quán.
/// - Ghi nhận kết quả các bàn đấu.
/// </summary>
[ApiController]
[Route("api/v1/pos/tournaments")]
[Authorize(Roles = nameof(UserRole.Manager))]
public class TournamentPosController : BaseApiController
{
    private readonly ITournamentService _tournamentService;

    public TournamentPosController(ITournamentService tournamentService)
    {
        _tournamentService = tournamentService;
    }

    /// <summary>
    /// Tạo giải đấu Splendor mới. [Role: Manager — phải là ManagerId của cafe.]
    /// </summary>
    /// <param name="cafeId">Mã quán cafe.</param>
    /// <param name="request">Thông tin giải: tiêu đề, giờ bắt đầu, cấu hình karma.</param>
    /// <response code="201">Tạo giải thành công (trạng thái Draft).</response>
    /// <response code="400">Dữ liệu không hợp lệ (ví dụ: MaxParticipants không phải bội số của 4).</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy game Splendor.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("cafes/{cafeId:guid}")]
    public async Task<IActionResult> CreateTournament(Guid cafeId, [FromBody] CreateTournamentRequestDto request)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.CreateTournamentAsync(managerId, cafeId, request);
        return this.NewResponse(201, ApiSuccessMessages.Tournament.Created, result);
    }

    /// <summary>
    /// Cập nhật thông tin giải khi còn ở trạng thái Draft. [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="request">Thông tin cập nhật (partial).</param>
    /// <response code="200">Cập nhật thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải.</response>
    /// <response code="409">Giải không ở trạng thái Draft.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPatch("{tournamentId:guid}")]
    public async Task<IActionResult> UpdateTournament(Guid tournamentId, [FromBody] UpdateTournamentRequestDto request)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.UpdateTournamentAsync(managerId, tournamentId, request);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.Updated, result);
    }

    /// <summary>
    /// Lấy danh sách giải của 1 quán, có thể filter theo trạng thái. [Role: Manager]
    /// </summary>
    /// <param name="cafeId">Mã quán cafe.</param>
    /// <param name="status">Filter trạng thái (Draft / RegistrationOpen / RegistrationClosed / OnGoing / Completed / Cancelled).</param>
    /// <response code="200">Danh sách giải đấu.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("cafes/{cafeId:guid}")]
    public async Task<IActionResult> GetCafeTournaments(Guid cafeId, [FromQuery] string? status)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.GetCafeTournamentsAsync(cafeId, managerId, status);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.ListRetrieved, result);
    }

    /// <summary>
    /// Manager dashboard: danh sách tournament đang OnGoing của cafe.
    /// Sort theo CurrentRound desc (round cao nhất trước), tiebreak theo StartTime asc.
    /// [Role: Manager — phải là chủ quán]
    /// </summary>
    /// <param name="cafeId">Mã quán cafe.</param>
    /// <response code="200">Danh sách tournament OnGoing của cafe.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("cafes/{cafeId:guid}/active")]
    public async Task<IActionResult> GetCafeActiveTournaments(Guid cafeId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.GetCafeActiveTournamentsAsync(cafeId, managerId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.ListRetrieved, result);
    }

    /// <summary>
    /// Mở đăng ký cho giải (Draft → RegistrationOpen). [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Mở đăng ký thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải.</response>
    /// <response code="409">Giải không ở trạng thái Draft.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/open-registration")]
    public async Task<IActionResult> OpenRegistration(Guid tournamentId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.OpenRegistrationAsync(managerId, tournamentId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.RegistrationOpened, result);
    }

    /// <summary>
    /// Mở lại đăng ký (RegistrationClosed → RegistrationOpen). [Role: Manager]
    /// Dùng khi: close nhầm, muốn tuyển thêm người sau khi auto-extend không đủ.
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Mở lại đăng ký thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải.</response>
    /// <response code="409">Giải không ở trạng thái RegistrationClosed.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/reopen-registration")]
    public async Task<IActionResult> ReopenRegistration(Guid tournamentId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.ReopenRegistrationAsync(managerId, tournamentId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.RegistrationReopened, result);
    }

    /// <summary>
    /// Đóng đăng ký (RegistrationOpen → RegistrationClosed). [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Đóng đăng ký thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải.</response>
    /// <response code="409">Giải không ở trạng thái RegistrationOpen.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/close-registration")]
    public async Task<IActionResult> CloseRegistration(Guid tournamentId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.CloseRegistrationAsync(managerId, tournamentId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.RegistrationClosed, result);
    }

    /// <summary>
    /// Bắt đầu giải đấu: tự động ghép bàn Swiss round 1. [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Bắt đầu thành công.</response>
    /// <response code="400">Chưa đóng đăng ký.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải.</response>
    /// <response code="409">Không đủ người check-in.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/start")]
    public async Task<IActionResult> StartTournament(Guid tournamentId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.StartTournamentAsync(managerId, tournamentId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.Started, result);
    }

    /// <summary>
    /// Hủy giải đấu (chỉ khi chưa OnGoing). [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="reason">Lý do hủy (bắt buộc nếu có người đã đăng ký).</param>
    /// <response code="200">Hủy thành công.</response>
    /// <response code="400">Thiếu lý do hủy.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải.</response>
    /// <response code="409">Giải đã OnGoing hoặc đã Completed.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/cancel")]
    public async Task<IActionResult> CancelTournament(Guid tournamentId, [FromQuery] string? reason)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.CancelTournamentAsync(managerId, tournamentId, reason);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.Cancelled, result);
    }

    /// <summary>
    /// Hoàn thành giải đấu (sau khi Final đã có kết quả). Áp dụng Karma bonus. [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Hoàn thành thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải.</response>
    /// <response code="409">Final chưa hoàn thành hoặc giải không OnGoing.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/complete")]
    public async Task<IActionResult> CompleteTournament(Guid tournamentId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.CompleteTournamentAsync(managerId, tournamentId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.Completed, result);
    }

    // === Check-in endpoints ===

    /// <summary>
    /// Check-in người chơi tại quán khi giải bắt đầu. [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="participantId">Mã participant cần check-in.</param>
    /// <response code="200">Check-in thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải hoặc participant.</response>
    /// <response code="409">Đã check-in rồi.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/participants/{participantId:guid}/check-in")]
    public async Task<IActionResult> CheckInParticipant(Guid tournamentId, Guid participantId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.CheckInParticipantAsync(managerId, tournamentId, participantId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.CheckedIn, result);
    }

    /// <summary>
    /// Thêm walk-in participant (khách vãng lai, không có tài khoản BoardVerse). [Role: Manager]
    /// Cho phép tạo khi giải đang ở RegistrationOpen / RegistrationClosed / OnGoing (chỉ khi R1 chưa Completed).
    /// Walk-in không nhận Karma bonus / Elo sync sau khi giải hoàn thành (BR-13/14 mirror).
    /// Thực tế board game cafe: sau khi R1 hoàn thành thì không nhận walk-in nữa (giữ fairness).
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="request">Tên hiển thị cho khách (vd: "Khách vãng lai #1").</param>
    /// <response code="201">Tạo walk-in thành công, trả về TournamentParticipantResponseDto.</response>
    /// <response code="400">Thiếu DisplayName.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải.</response>
    /// <response code="409">Giải đã Completed/Cancelled, R1 đã hoàn thành (không thể thêm walk-in), đã có Final, round đang diễn ra, hoặc trùng DisplayName.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/walk-in")]
    public async Task<IActionResult> AddWalkInParticipant(Guid tournamentId, [FromBody] AddWalkInParticipantRequestDto request)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.ManagerAddWalkInParticipantAsync(managerId, tournamentId, request);
        return this.NewResponse(201, ApiSuccessMessages.Tournament.WalkInAdded, result);
    }

    /// <summary>
    /// Đánh dấu người chơi không đến (no-show). [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="participantId">Mã participant.</param>
    /// <response code="200">Đánh dấu no-show thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy giải hoặc participant.</response>
    /// <response code="409">Participant đã Finished.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/participants/{participantId:guid}/no-show")]
    public async Task<IActionResult> MarkNoShow(Guid tournamentId, Guid participantId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.MarkNoShowAsync(managerId, tournamentId, participantId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.ParticipantStatusUpdated, result);
    }

    // === Match endpoints ===

    /// <summary>
    /// Bắt đầu 1 bàn đấu (Scheduled → OnGoing). [Role: Manager]
    /// </summary>
    /// <param name="matchId">Mã bàn đấu.</param>
    /// <response code="200">Bắt đầu bàn đấu thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy bàn đấu.</response>
    /// <response code="409">Bàn đấu đã bắt đầu hoặc đã kết thúc.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("matches/{matchId:guid}/start")]
    public async Task<IActionResult> StartMatch(Guid matchId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.StartMatchAsync(managerId, matchId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.MatchStarted, result);
    }

    /// <summary>
    /// Ghi nhận kết quả bàn đấu: điểm từng người + người thắng. [Role: Manager]
    /// </summary>
    /// <param name="matchId">Mã bàn đấu (từ URL).</param>
    /// <param name="request">Điểm từng người chơi + người thắng.</param>
    /// <response code="200">Ghi nhận kết quả thành công.</response>
    /// <response code="400">Winner không nằm trong 4 người chơi.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải Manager của cafe.</response>
    /// <response code="404">Không tìm thấy bàn đấu.</response>
    /// <response code="409">Bàn đấu không ở trạng thái hợp lệ.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("matches/{matchId:guid}/result")]
    public async Task<IActionResult> RecordMatchResult(Guid matchId, [FromBody] RecordMatchResultRequestDto request)
    {
        if (request.MatchId != matchId)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.MatchIdBodyMismatch);
        }
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.RecordMatchResultAsync(managerId, matchId, request);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.MatchResultRecorded, result);
    }

    /// <summary>
    /// Sửa kết quả bàn đấu đã ghi nhận (case nhập sai điểm / sai winner tại POS).
    /// Chỉ cho phép khi bàn đã Completed, chưa build round kế tiếp, và không phải Final.
    /// Revert Swiss score + Elo rồi apply lại với data mới.
    /// Final không cho sửa (đã sync Karma + Elo xong). [Role: Manager — phải là chủ quán tạo tournament.]
    /// </summary>
    /// <param name="matchId">Mã bàn đấu.</param>
    /// <param name="request">Winner mới + điểm mới + lý do sửa (audit trail bắt buộc).</param>
    /// <response code="200">Sửa kết quả thành công.</response>
    /// <response code="400">Thiếu lý do, winner không thuộc match, hoặc score không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ quán tạo tournament.</response>
    /// <response code="404">Không tìm thấy bàn đấu.</response>
    /// <response code="409">Bàn chưa Completed, là Final, hoặc đã có round kế tiếp.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPatch("matches/{matchId:guid}/result")]
    public async Task<IActionResult> UpdateMatchResult(Guid matchId, [FromBody] UpdateMatchResultRequestDto request)
    {
        if (request.MatchId != matchId)
        {
            throw new BadRequestException(ApiErrorMessages.Tournament.MatchIdBodyMismatch);
        }
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.UpdateMatchResultAsync(managerId, matchId, request);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.MatchResultCorrected, result);
    }

    /// <summary>
    /// Hủy một ván đấu tournament (ví dụ: bàn thiếu người, dispute không giải quyết được). [Role: Manager — phải là chủ quán tạo tournament.]
    /// </summary>
    /// <param name="matchId">Mã ván đấu.</param>
    /// <param name="request">Lý do hủy ván đấu (bắt buộc, lưu vào log audit).</param>
    /// <response code="200">Hủy ván đấu thành công.</response>
    /// <response code="400">Thiếu lý do hoặc ván đấu đã hoàn thành.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ quán tạo tournament.</response>
    /// <response code="404">Không tìm thấy ván đấu.</response>
    /// <response code="409">Ván đấu đã hoàn thành, không thể hủy.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("matches/{matchId:guid}/cancel")]
    public async Task<IActionResult> CancelMatch(Guid matchId, [FromBody] CancelMatchRequestDto request)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.CancelMatchAsync(managerId, matchId, request.Reason);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.MatchCancelled, result);
    }

/// <summary>
    /// Chuyển sang vòng đấu kế tiếp sau khi vòng hiện tại đã hoàn tất toàn bộ bàn đấu.
    /// Manager trigger thủ công sau khi tất cả các bàn của Round hiện tại đã ghi nhận kết quả.
    /// Round kế tiếp tự động build (Swiss nếu &lt; PreliminaryRounds; Final nếu đã qua PreliminaryRounds).
    /// [Role: Manager — phải là chủ quán tạo tournament.]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <response code="200">Chuyển vòng thành công; Round kế tiếp đã được build.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ quán tạo tournament.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Giải không ở trạng thái OnGoing; Round hiện tại chưa kết thúc toàn bộ; hoặc đã đến vòng cuối.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/advance-round")]
    public async Task<IActionResult> AdvanceRound(Guid tournamentId)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.AdvanceRoundAsync(managerId, tournamentId);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.RoundAdvanced, result);
    }

    // === Manual Pairing endpoints ===

    /// <summary>
    /// Đổi pairing mode (Auto/Manual) cho tournament. [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="request">Body chứa Mode: Auto (0) hoặc Manual (1).</param>
    /// <response code="200">Đổi mode thành công.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ quán tạo tournament.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Tournament đã OnGoing và round hiện tại đã có matches — không thể chuyển sang Manual.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/pairing-mode")]
    public async Task<IActionResult> SetPairingMode(Guid tournamentId, [FromBody] SetPairingModeRequestDto request)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.SetPairingModeAsync(managerId, tournamentId, request.Mode);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.Updated, result);
    }

    /// <summary>
    /// Preview pairings cho 1 round (chưa save). Trả về auto-suggested pairings
    /// hoặc manual pairings hiện tại nếu đã có. [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="roundNumber">Vòng cần preview (1-3 cho Swiss, 4 cho Final).</param>
    /// <response code="200">Danh sách pairings preview.</response>
    /// <response code="400">RoundNumber không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ quán tạo tournament.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("{tournamentId:guid}/pairings/{roundNumber:int}/preview")]
    public async Task<IActionResult> PreviewPairings(Guid tournamentId, int roundNumber)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.PreviewPairingsAsync(managerId, tournamentId, roundNumber);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.PairingsPreviewed, result);
    }

    /// <summary>
    /// Manager lưu manual pairings cho 1 round (override auto Swiss pairing). [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="request">RoundNumber (1-4) + danh sách pairings (MatchNumber + PlayerIds).</param>
    /// <response code="200">Lưu manual pairings thành công.</response>
    /// <response code="400">RoundNumber không hợp lệ; trùng MatchNumber; trùng PlayerId; UserId không thuộc tournament; bàn không đủ 2-4 người.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ quán tạo tournament.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Round đã có matches — không thể set manual pairings.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("{tournamentId:guid}/pairings")]
    public async Task<IActionResult> SetRoundPairings(Guid tournamentId, [FromBody] SetRoundPairingsRequestDto request)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.SetRoundPairingsAsync(managerId, tournamentId, request);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.PairingsUpdated, result);
    }

    /// <summary>
    /// Xóa manual pairings cho 1 round (quay lại dùng auto). [Role: Manager]
    /// </summary>
    /// <param name="tournamentId">Mã giải đấu.</param>
    /// <param name="roundNumber">Vòng cần reset (1-4).</param>
    /// <response code="200">Reset pairings thành công. Response chứa auto-suggested pairings.</response>
    /// <response code="400">RoundNumber không hợp lệ.</response>
    /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
    /// <response code="403">Không phải chủ quán tạo tournament.</response>
    /// <response code="404">Không tìm thấy giải đấu.</response>
    /// <response code="409">Round đã có matches — không thể reset.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete("{tournamentId:guid}/pairings/{roundNumber:int}")]
    public async Task<IActionResult> ClearRoundPairings(Guid tournamentId, int roundNumber)
    {
        var managerId = GetUserIdFromClaims();
        var result = await _tournamentService.ClearRoundPairingsAsync(managerId, tournamentId, roundNumber);
        return this.NewResponse(200, ApiSuccessMessages.Tournament.PairingsUpdated, result);
    }
}