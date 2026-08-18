using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    /// <summary>
    /// **SHARED — dùng cho CẢ Flow A (Reservation) và Flow B (Booking).**
    /// Lobby là entity dùng chung cho 2 flow đặt chỗ. Mỗi Lobby có FK cả `ReservationId` (Flow A)
    /// lẫn `BookingId` (Flow B, legacy). Endpoint `POST /api/v1/lobbies` đã deprecated —
    /// Flow A tạo Lobby atomically qua `ReservationService.ConfirmAsync`, không qua đây.
    /// </summary>
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
        /// Endpoint cũ — DEPRECATED. Tạo lobby phải qua
        /// <c>POST /api/v1/reservations/quote</c> + <c>POST /api/v1/reservations/confirm</c>
        /// (BR §XXI-B.1). Endpoint này sẽ bị xóa sau khi mobile app rollout.
        /// </summary>
        /// <response code="410">Endpoint đã deprecated, dùng ReservationController.</response>
        [HttpPost]
        [Obsolete("Dùng POST /api/v1/reservations/confirm thay thế. BR §XXI-B.1.")]
        public IActionResult CreateLobby([FromBody] CreateLobbyRequestDto request)
        {
            return this.NewResponse(
                410,
                "EndpointDeprecated",
                new
                {
                    message = "Tạo lobby phải qua flow reservation mới (BVC). Hãy dùng POST /api/v1/reservations/quote → POST /api/v1/reservations/confirm.",
                    newEndpoint = "POST /api/v1/reservations/confirm",
                    deprecatedAt = DateTime.UtcNow
                });
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
        /// BR-USER-LIMIT-02: excludeSelfOverlapping loại bỏ các lobby trùng lịch với user.
        /// </summary>
        /// <param name="request">GameTemplateId bắt buộc; latitude/longitude/radiusKm tùy chọn; minKarmaScore tùy chọn; excludeSelfOverlapping.</param>
        /// <response code="200">Danh sách phòng chờ public phù hợp, có kèm DistanceKm khi search geo.</response>
        /// <response code="400">Thiếu gameTemplateId hoặc tham số không hợp lệ.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("search")]
        public async Task<IActionResult> SearchLobbies([FromBody] SearchLobbiesRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.SearchLobbiesAsync(request, userId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.LobbiesRetrieved, result);
        }

        /// <summary>
        /// Khám phá các lobby public đang mở (status=Open, IsPrivate=false) để player khác có thể thấy và join.
        /// Hỗ trợ filter optional theo game và khoảng cách địa lý.
        /// Đây là API dành cho màn hình "Browse lobbies" trên mobile — không bắt buộc gameTemplateId như /search. [Role: Player]
        /// BR-USER-LIMIT-02: excludeSelfOverlapping loại bỏ các lobby trùng lịch với user.
        /// </summary>
        /// <param name="gameTemplateId">Optional: chỉ lấy lobby của game này.</param>
        /// <param name="latitude">Optional: latitude của user (kết hợp longitude + radiusKm).</param>
        /// <param name="longitude">Optional: longitude của user.</param>
        /// <param name="radiusKm">Optional: chỉ lấy lobby trong bán kính này (km).</param>
        /// <param name="limit">Số lobby tối đa trả về (1-100, default 50).</param>
        /// <param name="excludeSelfOverlapping">Loại bỏ lobby trùng lịch với user (BR-USER-LIMIT-02).</param>
        /// <response code="200">Danh sách lobby public đang mở, có kèm DistanceKm khi filter theo geo.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("discoverable")]
        public async Task<IActionResult> GetDiscoverableLobbies(
            [FromQuery] Guid? gameTemplateId,
            [FromQuery] double? latitude,
            [FromQuery] double? longitude,
            [FromQuery] double? radiusKm,
            [FromQuery] int limit = 50,
            [FromQuery] bool excludeSelfOverlapping = false)
        {
            if (limit < 1 || limit > 100)
            {
                throw new BadRequestException(ApiErrorMessages.Validation.LobbySearchLimitRange);
            }

            // Nếu truyền 1 trong 3 tham số geo thì bắt buộc cả 3
            var geoProvided = new[] { latitude.HasValue, longitude.HasValue, radiusKm.HasValue };
            if (geoProvided.Any(x => x) && !geoProvided.All(x => x))
            {
                throw new BadRequestException(ApiErrorMessages.Validation.LobbySearchGeoRequired);
            }

            if (radiusKm.HasValue && (radiusKm.Value <= 0 || radiusKm.Value > 500))
            {
                throw new BadRequestException(ApiErrorMessages.Validation.LobbySearchRadiusRange);
            }

            Guid? requestingUserId = excludeSelfOverlapping ? GetUserIdFromClaims() : null;
            var result = await _lobbyService.GetDiscoverableLobbiesAsync(
                gameTemplateId, latitude, longitude, radiusKm, limit, requestingUserId);
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
        /// Host giải tán lobby — soft delete (row vẫn còn để phục vụ audit + risk signals).
        /// Tính refund BVC theo BR-REFUND-02/03 (grace 15p / 24h / 6h trước giờ chơi) + giải phóng
        /// SeatInventory/GameInventory atomic với status flip. Chỉ host mới được gọi. Không áp dụng
        /// khi lobby đã check-in tại quán, đã đóng, hoặc đã terminal. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">Lý do giải tán (optional).</param>
        /// <response code="200">Phòng chờ đã giải tán. Response có kèm RefundBvc/ForfeitBvc/RefundPolicyApplied.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Phòng đã đóng hoặc đang trong phiên chơi, không thể giải tán.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpDelete("{lobbyId:guid}")]
        public async Task<IActionResult> DissolveLobby(Guid lobbyId, [FromBody] DissolveLobbyRequestDto? request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.DissolveLobbyAsync(lobbyId, userId, request?.Reason);
            return this.NewResponse(200, "Phòng chờ đã được giải tán.", result);
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
        /// Member bấm Ready/Unready để xác nhận tham gia lobby. Cho phép Ready khi lobby còn
        /// Open/Full/Viable (không bắt buộc phải Full). Nếu TẤT CẢ member Ready → lobby chuyển InProgress.
        /// Khi lobby vừa đạt MaxMembers (chuyển sang Full), BR-LOBBY-READY-03 ghi nhận FullAt;
        /// scheduler sẽ timeout nếu 20 phút sau vẫn chưa có ai Ready. [Role: Player]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">isReady = true (sẵn sàng) hoặc false (hủy).</param>
        /// <response code="200">Trạng thái ready đã cập nhật. Trả về lobby dto với status mới nhất (Full/InProgress nếu đủ điều kiện).</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải member.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="409">Lobby đã đóng (TimeoutFailed/HostCancelled/Closed) hoặc member đã bị Kicked/Left.</response>
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
        /// Lấy tất cả lobby của user (host hoặc member, active). [Role: Player]
        /// </summary>
        /// <response code="200">Danh sách lobby của user.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("my")]
        public async Task<IActionResult> GetMyLobbies()
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.GetMyLobbiesAsync(userId);
            return this.NewResponse(200, "Lấy danh sách phòng chờ của bạn.", result);
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
        /// Lấy lịch sử chat trong lobby (cursor pagination). [Role: Player — host hoặc active member]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="beforeCursor">Lấy message trước thời điểm này (ISO 8601).</param>
        /// <param name="limit">Số lượng tối đa (1-200, default 50).</param>
        /// <response code="200">Danh sách tin nhắn sắp xếp tăng dần theo thời gian.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải host hoặc active member.</response>
        /// <response code="404">Không tìm thấy phòng chờ.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpGet("{lobbyId:guid}/messages")]
        public async Task<IActionResult> GetMessages(Guid lobbyId, [FromQuery] DateTime? beforeCursor, [FromQuery] int limit = 50)
        {
            var userId = GetUserIdFromClaims();
            var lobby = await _lobbyService.GetLobbyAsync(lobbyId, userId);

            // Allow host OR any active member
            var canView = lobby.HostUserId == userId
                || lobby.Members.Any(m => m.UserId == userId && m.IsActive);

            if (!canView)
            {
                throw new ForbiddenException(ApiErrorMessages.Lobby.Message.NotLobbyMember);
            }

            var result = await _lobbyMessageService.GetMessagesAsync(lobbyId, beforeCursor, limit);
            return this.NewResponse(200, "Lấy lịch sử tin nhắn.", result);
        }

        /// <summary>
        /// L-03: Host tạo lại mã chia sẻ (invalidate mã cũ, sinh mã mới unique).
        /// Dùng khi mã bị leak hoặc muốn reset. Chỉ áp dụng khi lobby đang Open hoặc Full. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Đã tạo mã chia sẻ mới, trả về thông tin lobby kèm ShareCode mới.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy lobby.</response>
        /// <response code="409">Lobby không trong trạng thái cho phép (Open/Full).</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{lobbyId:guid}/share-code/regenerate")]
        public async Task<IActionResult> RegenerateShareCode(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.RegenerateShareCodeAsync(lobbyId, userId);
            return this.NewResponse(200, ApiSuccessMessages.Lobby.ShareCodeRegenerated, result);
        }

        /// <summary>
        /// BR-NEW-14 (b): Host đổi timeSlot và/hoặc preferred times của lobby.
        /// Chỉ áp dụng khi lobby chưa check-in (status = Open/Viable/Full/PendingCafeApproval).
        /// Recalculate RecruitmentDeadline theo newTimeSlot/preferredTimes. [Role: Player — chỉ Host]
        /// BR-RES-07/08/09: preferredStartTime/EndTime phải nằm trong slot range.
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <param name="request">
        /// - newTimeSlot: khung giờ mới (morning/afternoon/evening/night), nullable = giữ nguyên
        /// - preferredStartTime: giờ bắt đầu ưu tiên (HH:mm), nullable = giữ nguyên
        /// - preferredEndTime: giờ kết thúc ưu tiên (HH:mm), nullable = giữ nguyên
        /// </param>
        /// <response code="200">Đã cập nhật thành công.</response>
        /// <response code="400">
        /// - TimeSlot trùng với hiện tại (khi newTimeSlot = current)
        /// - Buffer không đủ 60 phút
        /// - preferredStartTime/EndTime nằm ngoài slot range
        /// </response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy lobby.</response>
        /// <response code="409">Lobby đã đóng/đang chơi hoặc không thể cập nhật.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/change-timeslot")]
        public async Task<IActionResult> ChangeTimeSlot(Guid lobbyId, [FromBody] ChangeTimeSlotRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.ChangeTimeSlotAsync(lobbyId, userId, request);
            return this.NewResponse(200, "Đã cập nhật thời gian thành công.", result);
        }

        /// <summary>
        /// BR-NEW-14 (d): Boost lobby — tăng visibility trong search/discovery.
        /// Chỉ áp dụng khi lobby đang Open. Cooldown 6 giờ giữa các lần boost. [Role: Player — chỉ Host]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ.</param>
        /// <response code="200">Đã boost phòng chờ thành công.</response>
        /// <response code="401">Thiếu token.</response>
        /// <response code="403">Không phải Host.</response>
        /// <response code="404">Không tìm thấy lobby.</response>
        /// <response code="409">Lobby không mở hoặc đang trong cooldown.</response>
        /// <response code="500">Lỗi hệ thống.</response>
        [HttpPost("{lobbyId:guid}/boost")]
        public async Task<IActionResult> BoostLobby(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _lobbyService.BoostLobbyAsync(lobbyId, userId);
            return this.NewResponse(200, "Đã boost phòng chờ. Phòng của bạn sẽ hiện ở vị trí cao hơn trong kết quả tìm kiếm!", result);
        }
    }

    public class CloseLobbyRequestDto
    {
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Request body cho dissolve lobby (DELETE /api/v1/lobbies/{lobbyId}).
    /// </summary>
    public class DissolveLobbyRequestDto
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