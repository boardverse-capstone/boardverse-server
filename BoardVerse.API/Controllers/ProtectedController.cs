using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProtectedController : BaseApiController
    {
        /// <summary>
        /// Truy cập endpoint yêu cầu xác thực để xác minh token hợp lệ.
        /// </summary>
        /// <response code="200">Người dùng đã truy cập được endpoint bảo vệ.</response>
        /// <response code="401">Thiếu token hoặc token không hợp lệ.</response>
        [HttpGet("secret")]
        [Authorize]
        public IActionResult Secret()
        {
            var userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var email = User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var data = new { userId, email };
            return this.NewResponse(200, "You have accessed a protected endpoint.", data);
        }
    }
}
