using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/lobbies")]
    public class LobbyController : BaseApiController
    {
        private readonly IKarmaRatingService _karmaRatingService;

        public LobbyController(IKarmaRatingService karmaRatingService)
        {
            _karmaRatingService = karmaRatingService;
        }

        /// <summary>
        /// Mở cửa sổ đánh giá karma sau khi POS hoàn tất thanh toán — phục vụ push AC 3.1. [Role: Manager, CafeStaff, Admin]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ gắn với phiên chơi đã thanh toán.</param>
        /// <response code="200">Cửa sổ đánh giá đã mở; trả về memberUserIds để client/mobile phát push notification.</response>
        /// <response code="400">Phòng chưa đủ điều kiện mở đánh giá (chưa InProgress hoặc Closed).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không đủ quyền vận hành.</response>
        /// <response code="404">Không tìm thấy phòng.</response>
        /// <response code="409">Cửa sổ đánh giá đã được mở trước đó.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{lobbyId:guid}/karma-rating/open")]
        [Authorize(Roles = "Manager,CafeStaff,Admin")]
        public async Task<IActionResult> OpenKarmaRatingWindow(Guid lobbyId)
        {
            var result = await _karmaRatingService.OpenLobbyKarmaRatingWindowAsync(lobbyId);
            return NewResponse(200, ApiSuccessMessages.Lobby.KarmaRatingWindowOpened, result);
        }
    }
}
