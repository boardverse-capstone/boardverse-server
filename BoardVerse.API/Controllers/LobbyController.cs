using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
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
        private readonly ILobbyMessageService _lobbyMessageService;

        public LobbyController(
            ILobbyService lobbyService,
            IKarmaRatingService karmaRatingService,
            ILobbyMessageService lobbyMessageService)
        {
            _lobbyService = lobbyService;
            _karmaRatingService = karmaRatingService;
            _lobbyMessageService = lobbyMessageService;
        }

        /// <summary>
        /// Tạo phòng chờ mới. [Role: Player]
        /// </summary>
        /// <param name="request">Thông tin phòng chờ: game, giờ chơi, sức chứa, visibility (public/private), description, cover image, cafeId, bookingId.</param>
        /// <response code="201">Phòng chờ đã tạo (kèm share code cho cả public/private).</response>
        /// <response code="400">Dữ liệu request không hợp lệ (vd: MaxMembers ngoài [GameTemplate.MinPlayers, MaxPlayers]).</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền dùng booking này.</response>
        /// <response code="404">Không tìm thấy game hoặc booking.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost]
        public async Task<IActionResult> CreateLobby([FromBody] CreateLobbyRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.CreateLobbyAsync(userId, request);
            return this.NewResponse(201, ApiSuccessMessages.Lobby.LobbyCreated, result);
        }

        /// <summary>
        /// Tham gia phòng chờ public. Private lobby phải qua invite hoặc share code. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Đã tham gia phòng chờ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Private lobby cần invite hoặc share code.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Phòng đã đầy/đóng hoặc bạn đã là thành viên.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/join")]
        public async Task<IActionResult> JoinLobby(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.JoinLobbyAsync(lobbyId, userId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyJoined, result);
        }

        /// <summary>
        /// Rời phòng chờ. Nếu Host rời mà còn members khác → transfer host cho người join sớm nhất.
        /// Nếu Host rời mà lobby trống → lobby chuyển sang HostCancelled. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Đã rời phòng chờ (host có thể đã được transfer).</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="404">Không tìm thấy phòng chờ hoặc bạn không phải member.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/leave")]
        public async Task<IActionResult> LeaveLobby(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.LeaveLobbyAsync(lobbyId, userId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyLeft, result);
        }

        /// <summary>
        /// Tra cứu chi tiết phòng chờ. Private lobby chỉ hiển thị cho member/host. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Chi tiết phòng chờ và danh sách thành viên (kèm karma + avatar).</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không có quyền xem private lobby này.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("{lobbyId:guid}")]
        public async Task<IActionResult> GetLobby(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.GetLobbyAsync(lobbyId, userId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyRetrieved, result);
        }

        /// <summary>
        /// Tìm phòng chờ theo game + filter địa lý + karma. Private lobby bị ẩn khỏi search. [Role: Player]
        /// </summary>
        /// <param name="request">GameTemplateId bắt buộc; latitude/longitude/radiusKm tùy chọn; minKarmaScore tùy chọn.</param>
        /// <response code="200">Danh sách phòng chờ public phù hợp, có kèm DistanceKm khi search geo.</response>
        /// <response code="400">Thiếu gameTemplateId hoặc tham số không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("search")]
        public async Task<IActionResult> SearchLobbies([FromBody] SearchLobbiesRequestDto request)
        {
            var result = await _lobbyService.SearchLobbiesAsync(request);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbiesRetrieved, result);
        }

        /// <summary>
        /// Khám phá các lobby public đang mở (status=Open, IsPrivate=false) để player khác có thể thấy và join.
        /// Hỗ trợ filter optional theo game và khoảng cách địa lý.
        /// Đây là API dành cho màn hình "Browse lobbies" trên mobile — không bắt buộc gameTemplateId như /search. [Role: Player]
        /// </summary>
        /// <param name="gameTemplateId">Optional: chỉ lấy lobby của game này.</param>
        /// <param name="latitude">Optional: latitude của user (kết hợp longitude + radiusKm).</param>
        /// <param name="longitude">Optional: longitude của user.</param>
        /// <param name="radiusKm">Optional: chỉ lấy lobby trong bán kính này (km).</param>
        /// <param name="limit">Số lobby tối đa trả về (1-100, default 50).</param>
        /// <response code="200">Danh sách lobby public đang mở, có kèm DistanceKm khi filter theo geo.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("discoverable")]
        public async Task<IActionResult> GetDiscoverableLobbies(
            [FromQuery] Guid? gameTemplateId,
            [FromQuery] double? latitude,
            [FromQuery] double? longitude,
            [FromQuery] double? radiusKm,
            [FromQuery] int limit = 50)
        {
            if (limit < 1 || limit > 100)
            {
                throw new BadRequestException("Limit phải nằm trong khoảng 1-100.");
            }

            // Nếu truyền 1 trong 3 tham số geo thì bắt buộc cả 3
            var geoProvided = new[] { latitude.HasValue, longitude.HasValue, radiusKm.HasValue };
            if (geoProvided.Any(x => x) && !geoProvided.All(x => x))
            {
                throw new BadRequestException("latitude, longitude, radiusKm phải truyền đồng thời nếu muốn filter theo khu vực.");
            }

            if (radiusKm.HasValue && (radiusKm.Value <= 0 || radiusKm.Value > 500))
            {
                throw new BadRequestException("radiusKm phải nằm trong khoảng (0, 500] km.");
            }

            var result = await _lobbyService.GetDiscoverableLobbiesAsync(
                gameTemplateId, latitude, longitude, radiusKm, limit);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbiesRetrieved, result);
        }

        /// <summary>
        /// Host đóng phòng chờ. Auto-cancel tất cả pending invites. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">Lý do đóng (optional).</param>
        /// <response code="200">Phòng chờ đã đóng.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Phòng đã đóng trước đó.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/close")]
        public async Task<IActionResult> CloseLobby(Guid lobbyId, [FromBody] CloseLobbyRequestDto? request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.CloseLobbyAsync(lobbyId, userId, request?.Reason);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbyClosed, result);
        }

        /// <summary>
        /// Khóa phòng chờ để bắt đầu ghép đội. Chuyển OPEN → FULL.
        /// Phải đạt MinPlayers. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Phòng chờ đã khóa.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Phòng không ở trạng thái mở hoặc chưa đủ MinPlayers.</response>
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
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/open-karma-window")]
        public async Task<IActionResult> OpenKarmaWindow(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var lobby = await _lobbyService.GetLobbyAsync(lobbyId, userId);
            if (lobby.HostUserId != userId)
            {
                return Forbid();
            }
            var result = await _karmaRatingService.OpenLobbyKarmaRatingWindowAsync(lobbyId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.KarmaRatingWindowOpened, result);
        }

        // ============================ P1/P2 features ============================

        /// <summary>
        /// Host chuyển quyền host cho thành viên khác. [Role: Player — chỉ Host hiện tại]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">UserId nhận host mới.</param>
        /// <response code="200">Đã chuyển host.</response>
        /// <response code="400">Yêu cầu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy phòng chờ hoặc target user không phải member.</response>
        /// <response code="409">Phòng không ở trạng thái cho phép.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/transfer-host")]
        public async Task<IActionResult> TransferHost(Guid lobbyId, [FromBody] TransferHostRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.TransferHostAsync(lobbyId, userId, request.NewHostUserId);
            return this.NewResponse(200, "Đã chuyển quyền Host.", result);
        }

        /// <summary>
        /// Host kick thành viên khác khỏi lobby. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">UserId bị kick + lý do (optional).</param>
        /// <response code="200">Đã kick thành viên.</response>
        /// <response code="400">Host không thể tự kick mình.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy target.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/kick")]
        public async Task<IActionResult> KickMember(Guid lobbyId, [FromBody] KickMemberRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.KickMemberAsync(lobbyId, userId, request.TargetUserId, request.Reason);
            return this.NewResponse(200, "Đã kick thành viên.", result);
        }

        /// <summary>
        /// Host cập nhật thông tin lobby (description, MaxMembers, IsPrivate, ...) trước khi start. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">Các trường muốn cập nhật (null = giữ nguyên).</param>
        /// <response code="200">Đã cập nhật.</response>
        /// <response code="400">Dữ liệu không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Phòng đã đóng/đang chơi hoặc MaxMembers nhỏ hơn số thành viên hiện tại.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPatch("{lobbyId:guid}")]
        public async Task<IActionResult> UpdateLobby(Guid lobbyId, [FromBody] UpdateLobbyRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.UpdateLobbyAsync(lobbyId, userId, request);
            return this.NewResponse(200, "Đã cập nhật phòng chờ.", result);
        }

        /// <summary>
        /// Member bấm Ready/Unready khi lobby FULL. Nếu tất cả member Ready → lobby chuyển InProgress. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">isReady = true/false.</param>
        /// <response code="200">Trạng thái ready đã cập nhật.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải member.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Lobby chưa FULL hoặc member bị Kicked/Left.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/ready")]
        public async Task<IActionResult> SetReady(Guid lobbyId, [FromBody] SetReadyRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.SetMemberReadyAsync(lobbyId, userId, request.IsReady);
            return this.NewResponse(200, request.IsReady ? "Đã sẵn sàng." : "Đã hủy sẵn sàng.", result);
        }

        /// <summary>
        /// Lấy danh sách lobby do user này host (cả còn active lẫn đã đóng). [Role: Player]
        /// </summary>
        /// <response code="200">Danh sách lobby host.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("hosted")]
        public async Task<IActionResult> GetHostedLobbies()
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.GetLobbiesByHostAsync(userId);
            return this.NewResponse(200, "Lấy danh sách phòng chờ do bạn host.", result);
        }

        /// <summary>
        /// Lấy danh sách lobby mà user đang tham gia. [Role: Player]
        /// </summary>
        /// <response code="200">Danh sách lobby joined.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("joined")]
        public async Task<IActionResult> GetJoinedLobbies()
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.GetJoinedLobbiesAsync(userId);
            return this.NewResponse(200, "Lấy danh sách phòng chờ đang tham gia.", result);
        }

        /// <summary>
        /// Báo cáo phòng chờ vi phạm. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">Category + reason.</param>
        /// <response code="201">Báo cáo đã gửi.</response>
        /// <response code="400">Bạn là Host nên không thể report lobby mình.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/report")]
        public async Task<IActionResult> ReportLobby(Guid lobbyId, [FromBody] CreateLobbyReportDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.ReportLobbyAsync(lobbyId, userId, request);
            return this.NewResponse(201, "Báo cáo đã được gửi.", result);
        }

        // ============================ Chat ============================

        /// <summary>
        /// Gửi tin nhắn chat trong lobby. [Role: Player — chỉ active member]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">Nội dung (1-1000 ký tự).</param>
        /// <response code="201">Tin nhắn đã gửi.</response>
        /// <response code="400">Tin nhắn không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải member.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/messages")]
        public async Task<IActionResult> PostMessage(Guid lobbyId, [FromBody] PostLobbyMessageRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var msg = await _lobbyMessageService.SendMessageAsync(lobbyId, userId, request.Content);
            return this.NewResponse(201, "Đã gửi tin nhắn.", msg);
        }

        /// <summary>
        /// Lấy lịch sử chat trong lobby (cursor pagination). [Role: Player — chỉ active member]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="beforeCursor">Lấy message trước thời điểm này (ISO 8601).</param>
        /// <param name="limit">Số lượng tối đa (1-200, default 50).</param>
        /// <response code="200">Danh sách tin nhắn sắp xếp tăng dần theo thời gian.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải member.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("{lobbyId:guid}/messages")]
        public async Task<IActionResult> GetMessages(Guid lobbyId, [FromQuery] DateTime? beforeCursor, [FromQuery] int limit = 50)
        {
            var userId = GetUserIdFromClaims();
            var lobby = await _lobbyService.GetLobbyAsync(lobbyId, userId);
            if (!lobby.Members.Any(m => m.UserId == userId && m.IsActive))
            {
                return Forbid();
            }
            var result = await _lobbyMessageService.GetMessagesAsync(lobbyId, beforeCursor, limit);
            return this.NewResponse(200, "Lấy lịch sử tin nhắn.", result);
        }
    }

    public class CloseLobbyRequestDto
    {
        public string? Reason { get; set; }
    }

    public class TransferHostRequestDto
    {
        public Guid NewHostUserId { get; set; }
    }

    public class KickMemberRequestDto
    {
        public Guid TargetUserId { get; set; }
        public string? Reason { get; set; }
    }

    public class SetReadyRequestDto
    {
        public bool IsReady { get; set; }
    }

    public class PostLobbyMessageRequestDto
    {
        public string Content { get; set; } = string.Empty;
    }
}