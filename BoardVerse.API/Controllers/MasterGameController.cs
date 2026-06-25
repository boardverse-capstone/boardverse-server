using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
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
        /// <param name="searchTerm">Từ khóa tìm kiếm gần đúng (bỏ dấu, không phân biệt hoa thường).</param>
        /// <param name="categoryIds">Lọc theo thể loại (multi-select).</param>
        /// <param name="playerCount">Lọc theo số người chơi phù hợp.</param>
        /// <param name="playTimeRanges">Lọc khung thời gian: 1=&lt;30p, 2=30-60p, 3=&gt;60p.</param>
        /// <param name="cafeId">Tùy chọn — mã quán; khi có sẽ gắn cờ alreadyInInventory cho từng game.</param>
        /// <param name="excludeInInventory">Khi true kèm cafeId — chỉ trả game chưa có trong kho quán.</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 10).</param>
        /// <response code="200">Trả về danh sách board game master có phân trang.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có role Manager hoặc bị chặn/vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        public async Task<IActionResult> GetMasterGames(
            [FromQuery] string? searchTerm,
            [FromQuery] List<Guid>? categoryIds,
            [FromQuery] int? playerCount,
            [FromQuery] List<PlayTimeRange>? playTimeRanges,
            [FromQuery] Guid? cafeId,
            [FromQuery] bool excludeInInventory = false,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var query = new GetMasterGamesQuery
            {
                SearchTerm = searchTerm,
                CategoryIds = categoryIds,
                PlayerCount = playerCount,
                PlayTimeRanges = playTimeRanges,
                CafeId = cafeId,
                ExcludeInInventory = excludeInInventory,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            var result = await _gameTemplateService.GetMasterGamesAsync(query);
            return this.NewResponse(200, ApiSuccessMessages.MasterGame.ListRetrieved, result);
        }

        /// <summary>
        /// Lấy chi tiết một board game master kèm linh kiện và thể loại. [Role: Manager — yêu cầu đăng nhập với role Manager.]
        /// </summary>
        /// <param name="id">Mã định danh board game master (GameTemplates.Id).</param>
        /// <param name="cafeId">Tùy chọn — nếu có, trả về alreadyInInventory cho quán đó.</param>
        /// <response code="200">Trả về thông tin game master đầy đủ kèm components và categories.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Tài khoản không có role Manager hoặc bị chặn/vô hiệu hóa.</response>
        /// <response code="404">Không tìm thấy board game master.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetMasterGameById(Guid id, [FromQuery] Guid? cafeId)
        {
            var result = await _gameTemplateService.GetMasterGameByIdAsync(id, cafeId);
            return this.NewResponse(200, ApiSuccessMessages.MasterGame.Retrieved, result);
        }
    }
}
