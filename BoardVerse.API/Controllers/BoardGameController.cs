using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/board-games")]
    public class BoardGameController : BaseApiController
    {
        private readonly IBoardGameService _boardGameService;

        public BoardGameController(IBoardGameService boardGameService)
        {
            _boardGameService = boardGameService;
        }

        /// <summary>
        /// Lấy danh sách thể loại board game cho bộ lọc UI. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <response code="200">Trả về danh sách thể loại (id, name, slug, sortOrder).</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var result = await _boardGameService.GetCategoriesAsync();
            return NewResponse(200, ApiSuccessMessages.BoardGame.CategoriesRetrieved, result);
        }

        /// <summary>
        /// Tra cứu board game với fuzzy search và bộ lọc đa tiêu chí. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="search">Từ khóa tìm kiếm gần đúng (bỏ dấu tiếng Việt, không phân biệt hoa thường, hỗ trợ alias).</param>
        /// <param name="categoryIds">Query: category_ids — lọc thể loại (multi-select GUID).</param>
        /// <param name="playerCount">Query: player_count — số người chơi phù hợp (min đến max của game).</param>
        /// <param name="durationRange">Query: duration_range — Under30, ThirtyToSixty hoặc Over60 (multi-select).</param>
        /// <param name="pageNumber">Số trang (mặc định 1).</param>
        /// <param name="pageSize">Kích thước trang (mặc định 10, tối đa 100).</param>
        /// <response code="200">Trả về danh sách board game có phân trang.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet]
        public async Task<IActionResult> GetBoardGames(
            [FromQuery] string? search,
            [FromQuery(Name = "category_ids")] List<Guid>? categoryIds,
            [FromQuery(Name = "player_count")] int? playerCount,
            [FromQuery(Name = "duration_range")] List<PlayTimeRange>? durationRange,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var query = new GetBoardGamesQuery
            {
                Search = search,
                CategoryIds = categoryIds,
                PlayerCount = playerCount,
                DurationRange = durationRange,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            var result = await _boardGameService.SearchBoardGamesAsync(query);
            return NewResponse(200, ApiSuccessMessages.BoardGame.ListRetrieved, result);
        }

        /// <summary>
        /// Lấy chi tiết board game kèm danh sách linh kiện và thể loại. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="id">Mã định danh board game (GameTemplates.Id).</param>
        /// <response code="200">Trả về thông tin đầy đủ: ảnh, tên, mô tả, số người, thể loại, components.</response>
        /// <response code="404">Không tìm thấy board game hoặc game đã bị vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetBoardGameById(Guid id)
        {
            var result = await _boardGameService.GetBoardGameByIdAsync(id);
            return NewResponse(200, ApiSuccessMessages.BoardGame.Retrieved, result);
        }

        /// <summary>
        /// Lấy chi tiết board game (alias của GET theo id). [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="id">Mã định danh board game (GameTemplates.Id).</param>
        /// <response code="200">Trả về thông tin đầy đủ: ảnh, tên, mô tả, số người, thể loại, components.</response>
        /// <response code="404">Không tìm thấy board game hoặc game đã bị vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{id:guid}/details")]
        public async Task<IActionResult> GetBoardGameDetails(Guid id)
        {
            var result = await _boardGameService.GetBoardGameDetailsAsync(id);
            return NewResponse(200, ApiSuccessMessages.BoardGame.DetailsRetrieved, result);
        }

        /// <summary>
        /// Kiểm tra cấu hình số người chơi và các chế độ chơi khả dụng (Solo/Nhóm). [Role: Public]
        /// </summary>
        /// <param name="id">Mã định danh board game (GameTemplates.Id).</param>
        /// <response code="200">Trả về min/max người, supportsSoloPlay và danh sách playMode UI có thể hiển thị.</response>
        /// <response code="404">Không tìm thấy board game hoặc game đã bị vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{id:guid}/play-configuration")]
        public async Task<IActionResult> GetPlayConfiguration(Guid id)
        {
            var result = await _boardGameService.GetPlayConfigurationAsync(id);
            return NewResponse(200, ApiSuccessMessages.BoardGame.PlayConfigurationRetrieved, result);
        }

        /// <summary>
        /// Xác định điều hướng sau khi người chơi chọn chế độ Solo hoặc Nhóm. [Role: Public]
        /// </summary>
        /// <param name="id">Mã định danh board game (GameTemplates.Id).</param>
        /// <param name="request">playMode: Solo (0) hoặc Group (1).</param>
        /// <response code="200">Trả về navigationTarget (SoloBooking hoặc LobbyCreation) và roomConfiguration tương ứng.</response>
        /// <response code="400">Chọn Solo nhưng game có minPlayers &gt; 1.</response>
        /// <response code="404">Không tìm thấy board game hoặc game đã bị vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("{id:guid}/play-navigation")]
        public async Task<IActionResult> ResolvePlayNavigation(
            Guid id,
            [FromBody] ResolveGamePlayNavigationRequestDto request)
        {
            var result = await _boardGameService.ResolvePlayNavigationAsync(id, request);
            return NewResponse(200, ApiSuccessMessages.BoardGame.PlayNavigationResolved, result);
        }
    }
}
