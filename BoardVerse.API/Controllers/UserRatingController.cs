using BoardVerse.Core.DTOs.Rating;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/users/ratings")]
    [Authorize]
    public class UserRatingController : BaseApiController
    {
        private readonly IKarmaRatingService _karmaRatingService;

        public UserRatingController(IKarmaRatingService karmaRatingService)
        {
            _karmaRatingService = karmaRatingService;
        }

        /// <summary>
        /// Lấy ngữ cảnh đánh giá chéo cho một phòng — danh sách thành viên và tiêu chí tích chọn (AC 3.2). [Role: Player, Manager, CafeStaff, Admin]
        /// </summary>
        /// <param name="lobbyId">Mã phòng chờ (Lobby) sau khi phiên chơi kết thúc và thanh toán.</param>
        /// <response code="200">Trả về membersToRate, availableTags (OnTime, Civil, Friendly, Toxic, NoShow) và trạng thái alreadyRated.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Người gọi không phải thành viên active của phòng.</response>
        /// <response code="404">Không tìm thấy phòng.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("karma/lobbies/{lobbyId:guid}")]
        public async Task<IActionResult> GetLobbyKarmaRatingContext(Guid lobbyId)
        {
            var userId = GetUserIdFromClaims();
            var result = await _karmaRatingService.GetLobbyRatingContextAsync(userId, lobbyId);
            return NewResponse(200, "Lobby karma rating context retrieved successfully", result);
        }

        /// <summary>
        /// Gửi đánh giá chéo karma cho các thành viên cùng phòng — cập nhật điểm uy tín (AC 3.3). [Role: Player, Manager, CafeStaff, Admin]
        /// </summary>
        /// <param name="request">lobbyId và mảng ratings (targetUserId, tags). Mỗi cặp rater→target chỉ được gửi một lần trong phòng.</param>
        /// <response code="200">Đánh giá đã áp dụng; trả về karmaDeltaApplied và karma mới của từng người bị đánh giá.</response>
        /// <response code="400">Phòng chưa mở đánh giá, tự đánh giá, target không thuộc phòng, hoặc thiếu tag.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Người gọi không phải thành viên của phòng.</response>
        /// <response code="404">Không tìm thấy phòng hoặc profile người bị đánh giá.</response>
        /// <response code="409">Đã đánh giá người này trong phòng trước đó.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("karma")]
        public async Task<IActionResult> SubmitKarmaRatings([FromBody] SubmitKarmaRatingsRequestDto request)
        {
            var userId = GetUserIdFromClaims();
            var result = await _karmaRatingService.SubmitKarmaRatingsAsync(userId, request);
            return NewResponse(200, "Karma ratings submitted successfully", result);
        }
    }
}
