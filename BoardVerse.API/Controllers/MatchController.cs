using BoardVerse.Core.DTOs.Match;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/matches")]
    [Authorize]
    public class MatchController : BaseApiController
    {
        private readonly IMatchResultService _matchResultService;

        public MatchController(IMatchResultService matchResultService)
        {
            _matchResultService = matchResultService;
        }

        /// <summary>
        /// Lấy trạng thái nhập kết quả và đồng thuận của phòng (AC 4.1, 4.2). [Role: Player, Manager, CafeStaff, Admin]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ đang chơi game đối kháng/chiến thuật.</param>
        /// <response code="200">Trả về supportsMatchResults, submissions, consensusStatus và conflictReason nếu mâu thuẫn.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Người gọi không phải thành viên active của phòng.</response>
        /// <response code="404">Không tìm thấy phòng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("results/lobbies/{lobbyId:guid}")]
        public async Task<IActionResult> GetMatchResultStatus(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _matchResultService.GetMatchResultStatusAsync(userId, lobbyId);
            return NewResponse(200, ApiSuccessMessages.Match.StatusRetrieved, result);
        }

        /// <summary>
        /// Gửi hoặc cập nhật kết quả trận đấu (Thắng/Thua/Hòa) — kích hoạt Elo khi đồng thuận 100% (AC 4.2, 4.3). [Role: Player, Manager, CafeStaff, Admin]
        /// </summary>
        /// <param name="request">lobbyId và outcome (Win, Loss, Draw) từ góc nhìn người gửi.</param>
        /// <response code="200">Đã ghi nhận; trả awaiting/conflict hoặc finalized kèm eloUpdates khi đồng thuận.</response>
        /// <response code="400">Phòng chưa eligible, game không thuộc thể loại đối kháng, hoặc outcome không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Người gọi không phải thành viên của phòng.</response>
        /// <response code="404">Không tìm thấy phòng hoặc profile thiếu khi finalize.</response>
        /// <response code="409">Kết quả trận đã được finalize trước đó.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("results")]
        public async Task<IActionResult> SubmitMatchResult([FromBody] SubmitMatchResultRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _matchResultService.SubmitMatchResultAsync(userId, request);
            return NewResponse(200, ApiSuccessMessages.Match.ResultSubmitted, result);
        }
    }
}
