using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : BaseApiController
    {
        private readonly IHealthService _healthService;

        public HealthController(IHealthService healthService)
        {
            _healthService = healthService;
        }

        /// <summary>
        /// Kiểm tra trạng thái hoạt động tổng thể của API. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <response code="200">API đang hoạt động bình thường.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var data = new { status = "healthy" };
            return this.NewResponse(200, "API is operational", data);
        }

        /// <summary>
        /// Kiểm tra kết nối cơ sở dữ liệu và trả về số lượng người dùng hiện có. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <response code="200">Kết nối cơ sở dữ liệu thành công.</response>
        /// <response code="500">Không truy cập được cơ sở dữ liệu.</response>
        [HttpGet("db-info")]
        public async Task<IActionResult> GetDatabaseInfo()
        {
            var userCount = await _healthService.GetUserCountAsync();
            var data = new { status = "connected", userCount };
            return this.NewResponse(200, "Database connected", data);
        }

        /// <summary>
        /// Trả về phản hồi nhịp tim đơn giản để kiểm tra endpoint. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <response code="200">Endpoint phản hồi bình thường.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return this.NewResponse(200, "pong", new { message = "pong" });
        }
    }
}
