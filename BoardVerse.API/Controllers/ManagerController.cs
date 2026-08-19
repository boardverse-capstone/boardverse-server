using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/manager")]
    [Authorize(Roles = "Manager")]
    public class ManagerController : BaseApiController
    {
        private readonly ICafeService _cafeService;

        public ManagerController(ICafeService cafeService)
        {
            _cafeService = cafeService;
        }

        /// <summary>
        /// Lấy danh sách quán mà manager hiện tại sở hữu (kèm đầy đủ thông tin chi tiết: pricing, schedule, SePay raw, operational status, refund policy, staff count, audit timestamps). [Role: Manager]
        /// </summary>
        /// <response code="200">Trả về danh sách quán chi tiết (có thể rỗng nếu chưa sở hữu quán nào).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có role Manager hoặc bị chặn/vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("my-cafes")]
        [ProducesResponseType(typeof(IEnumerable<ManagerCafeDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMyCafes()
        {
            var managerId = GetUserIdFromClaims();
            var cafes = await _cafeService.GetManagerCafesAsync(managerId);
            return this.NewResponse(200, ApiSuccessMessages.Cafe.ListRetrieved, cafes);
        }
    }
}
