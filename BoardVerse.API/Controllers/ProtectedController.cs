using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProtectedController : BaseApiController
    {
        /// <summary>
        /// Truy cập endpoint yêu cầu xác thực để xác minh token hợp lệ. [Role: Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập.]
        /// </summary>
        /// <response code="200">Người dùng đã truy cập được endpoint bảo vệ.</response>
        /// <response code="401">Thiếu token, token hết hạn, token bị thu hồi, hoặc tài khoản bị chặn/vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("secret")]
        [Authorize]
        public IActionResult Secret()
        {
            var userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var email = User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;
            var data = new { userId, email };
            return this.NewResponse(200, "You have accessed a protected endpoint.", data);
        }
    }
}
