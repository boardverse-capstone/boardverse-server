using BoardVerse.Core.DTOs.Game;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/master-games")]
    [Authorize(Roles = "Manager")]
    public class MasterGameController : BaseApiController
    {
        private readonly IGameTemplateService _gameTemplateService;

        public MasterGameController(IGameTemplateService gameTemplateService)
        {
            _gameTemplateService = gameTemplateService;
        }

        /// <summary>
        /// Tra cứu danh mục board game master (tìm kiếm theo tên, kèm linh kiện mẫu). [Role: Manager — yêu cầu đăng nhập với role Manager.]
        /// </summary>
        /// <param name="searchTerm">Optional search term to filter games by name.</param>
        /// <param name="cafeId">Optional cafe ID — populates alreadyInInventory; use with excludeInInventory.</param>
        /// <param name="excludeInInventory">When true with cafeId, returns only games not yet in cafe inventory.</param>
        /// <param name="pageNumber">Page number (default: 1).</param>
        /// <param name="pageSize">Page size (default: 10).</param>
        /// <response code="200">Trả về danh sách board game master có phân trang.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có role Manager hoặc bị chặn/vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        public async Task<IActionResult> GetMasterGames(
            [FromQuery] string? searchTerm,
            [FromQuery] Guid? cafeId,
            [FromQuery] bool excludeInInventory = false,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var query = new GetMasterGamesQuery
            {
                SearchTerm = searchTerm,
                CafeId = cafeId,
                ExcludeInInventory = excludeInInventory,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            var result = await _gameTemplateService.GetMasterGamesAsync(query);
            return this.NewResponse(200, "Games retrieved successfully", result);
        }
    }
}
