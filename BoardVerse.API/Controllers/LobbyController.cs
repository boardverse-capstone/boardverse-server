using BoardVerse.Core.Messages;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/lobbies")]
    [Authorize]
    public class LobbyController : BaseApiController
    {
        private readonly ILobbyService _lobbyService;
        private readonly IKarmaRatingService _karmaRatingService;

        public LobbyController(ILobbyService lobbyService, IKarmaRatingService karmaRatingService)
        {
            _lobbyService = lobbyService;
            _karmaRatingService = karmaRatingService;
        }

        /// <summary>
        /// Tạo phòng chờ mới. [Role: Player]
        /// </summary>
        /// <param name="request">Thông tin phòng chờ: game, giờ chơi, sức chứa.</param>
        /// <response code="201">Phòng chờ đã tạo.</response>
        /// <response code="400">Dữ liệu request không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không đủ quyền.</response>
        /// <response code="404">Không tìm thấy game.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost]
        public async Task<IActionResult> CreateLobby([FromBody] CreateLobbyRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.CreateLobbyAsync(userId, request);
            return this.NewResponse(201, ApiSuccessMessages.Lobby.LobbyCreated, result);
        }

        /// <summary>
        /// Tham gia phòng chờ. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Đã tham gia phòng chờ.</response>
        /// <response code="400">Yêu cầu không hợp lệ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Phòng đã đầy hoặc bạn đã tham gia.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{lobbyId:guid}/join")]
        public async Task<IActionResult> JoinLobby(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.JoinLobbyAsync(lobbyId, userId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyJoined, result);
        }

        /// <summary>
        /// Rời phòng chờ. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Đã rời phòng chờ.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{lobbyId:guid}/leave")]
        public async Task<IActionResult> LeaveLobby(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.LeaveLobbyAsync(lobbyId, userId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyLeft, result);
        }

        /// <summary>
        /// Tra cứu chi tiết phòng chờ. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Chi tiết phòng chờ và danh sách thành viên.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không đủ quyền.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{lobbyId:guid}")]
        public async Task<IActionResult> GetLobby(Guid lobbyId)
        {
            var result = await _lobbyService.GetLobbyAsync(lobbyId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyRetrieved, result);
        }

        /// <summary>
        /// Tìm phòng chờ theo game. [Role: Player]
        /// </summary>
        /// <param name="request">Tựa game cần tìm.</param>
        /// <response code="200">Danh sách phòng chờ phù hợp.</response>
        /// <response code="400">Thiếu gameTemplateId.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không đủ quyền.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("search")]
        public async Task<IActionResult> SearchLobbies([FromBody] SearchLobbiesRequestDto request)
        {
            var result = await _lobbyService.SearchLobbiesAsync(request);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbiesRetrieved, result);
        }

        /// <summary>
        /// Đóng phòng chờ. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Phòng chờ đã đóng.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Chỉ Host mới đóng được phòng.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{lobbyId:guid}/close")]
        public async Task<IActionResult> CloseLobby(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.CloseLobbyAsync(lobbyId, userId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyClosed, result);
        }

        /// <summary>
        /// Khóa phòng chờ để bắt đầu ghép đội. [Role: Player — chỉ Host]
        /// Chuyển trạng thái OPEN → FULL.
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Phòng chờ đã khóa.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Chỉ Host mới khóa được phòng.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Phòng không ở trạng thái mở.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/lock")]
        public async Task<IActionResult> LockLobby(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.LockLobbyAsync(lobbyId, userId);
            return this.NewResponse(200, "Phòng chờ đã được khóa.", result);
        }

        /// <summary>
        /// Mở cửa sổ đánh giá Karma sau khi phiên chơi kết thúc. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Cửa sổ đánh giá đã mở.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Chỉ Host mới mở được.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/open-karma-window")]
        public async Task<IActionResult> OpenKarmaWindow(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var lobby = await _lobbyService.GetLobbyAsync(lobbyId);
            if (lobby.HostUserId != userId)
            {
                return Forbid();
            }
            // P4: Use KarmaRatingService which correctly transitions Lobby.Status to RatingOpen
            var result = await _karmaRatingService.OpenLobbyKarmaRatingWindowAsync(lobbyId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.KarmaRatingWindowOpened, result);
        }
    }
}
