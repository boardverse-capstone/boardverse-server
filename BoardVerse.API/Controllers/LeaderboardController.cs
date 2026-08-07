using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LeaderboardController : BaseApiController
    {
        private readonly ILeaderboardService _leaderboardService;

        public LeaderboardController(ILeaderboardService leaderboardService)
        {
            _leaderboardService = leaderboardService;
        }

        /// <summary>
        /// K-06: Global karma leaderboard, ordered by KarmaPoints DESC. [Role: Public — ai cũng xem được.]
        /// </summary>
        /// <param name="limit">Số lượng người chơi trả về (mặc định 100, tối đa 500).</param>
        /// <response code="200">Trả về danh sách người chơi xếp hạng theo karma.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("karma")]
        [AllowAnonymous]
        public async Task<IActionResult> GetKarmaLeaderboard([FromQuery] int limit = 100)
        {
            var safeLimit = Math.Clamp(limit, 1, 500);
            var result = await _leaderboardService.GetKarmaLeaderboardAsync(safeLimit);
            return Ok(result);
        }
    }
}
