using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/leaderboard")]
    public class LeaderboardController : BaseApiController
    {
        private readonly ILeaderboardService _leaderboardService;

        public LeaderboardController(ILeaderboardService leaderboardService)
        {
            _leaderboardService = leaderboardService;
        }

        /// <summary>
        /// K-06: Global karma leaderboard, ordered by KarmaPoints DESC. [Role: Public — ai cũng xem được.]
        /// Nếu user đã đăng nhập, response sẽ kèm thêm block <c>userRank</c> với thứ hạng hiện tại.
        /// </summary>
        /// <param name="top">Số lượng người chơi trả về (mặc định 50, tối đa 100).</param>
        /// <param name="offset">Bỏ qua N người đầu tiên (mặc định 0) — dùng khi cần phân trang.</param>
        /// <response code="200">Trả về danh sách người chơi xếp hạng theo karma.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("karma")]
        [AllowAnonymous]
        public async Task<IActionResult> GetKarmaLeaderboard(
            [FromQuery] int top = 50,
            [FromQuery] int offset = 0)
        {
            var safeTop = Math.Clamp(top, 1, 100);
            var safeOffset = Math.Max(0, offset);
            var viewer = GetOptionalViewerContext().UserId;
            var result = await _leaderboardService.GetKarmaLeaderboardPagedAsync(safeOffset, safeTop, viewer);
            return NewResponse(200, ApiSuccessMessages.Profile.KarmaStateRetrieved, result);
        }

        /// <summary>
        /// K-06: Global elo leaderboard, ordered by GlobalElo DESC. [Role: Public — ai cũng xem được.]
        /// Nếu user đã đăng nhập, response sẽ kèm thêm block <c>userRank</c>.
        /// </summary>
        /// <param name="top">Số lượng người chơi trả về (mặc định 50, tối đa 100).</param>
        /// <param name="offset">Bỏ qua N người đầu tiên (mặc định 0).</param>
        /// <response code="200">Trả về danh sách người chơi xếp hạng theo Global Elo.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("elo")]
        [AllowAnonymous]
        public async Task<IActionResult> GetEloLeaderboard(
            [FromQuery] int top = 50,
            [FromQuery] int offset = 0)
        {
            var safeTop = Math.Clamp(top, 1, 100);
            var safeOffset = Math.Max(0, offset);
            var viewer = GetOptionalViewerContext().UserId;
            var result = await _leaderboardService.GetEloLeaderboardPagedAsync(safeOffset, safeTop, viewer);
            return NewResponse(200, ApiSuccessMessages.Tournament.LeaderboardRetrieved, result);
        }
    }
}
