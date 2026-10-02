using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Images;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/board-games")]
    [AllowAnonymous] // Toàn bộ endpoint là public (categories, search, detail, play-config).
    public class BoardGameController : BaseApiController
    {
        private readonly IBoardGameService _boardGameService;
        private readonly IThumbnailProxyService _thumbnailProxy;

        public BoardGameController(
            IBoardGameService boardGameService,
            IThumbnailProxyService thumbnailProxy)
        {
            _boardGameService = boardGameService;
            _thumbnailProxy = thumbnailProxy;
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
        /// Lấy top 5 board game được chơi nhiều nhất trong hệ thống để hiển thị widget "Top hot" trên UI mobile bên player. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <response code="200">Trả về tối đa 5 board game kèm số lượt chơi (PlayCount) sắp xếp giảm dần.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("top5")]
        public async Task<IActionResult> GetTop5PlayedBoardGames()
        {
            var result = await _boardGameService.GetTopPlayedBoardGamesAsync(5);
            return NewResponse(200, ApiSuccessMessages.BoardGame.TopPlayedRetrieved, result);
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
        /// Lấy danh sách quán cafe đang ACTIVE có board game này trong kho và có thể chơi được.
        /// Đây là chiều ngược của <c>GET /api/cafes/{cafeId}/active-games</c> — player hỏi "chơi game này ở đâu?"
        /// Chỉ trả các quán thỏa mãn đồng thời:
        /// • Quán cafe tồn tại &amp; đang ACTIVE (IsActive=true, PartnerOperationalStatus=Active).
        /// • CafeGameInventory.IsActive = true (quán chưa xóa mềm khỏi kho).
        /// • GameTemplate.IsActive = true (master game vẫn active).
        /// • Status ∈ {Available, InUse} — không bao gồm Damaged/Maintenance/Retired.
        /// Hỗ trợ filter: name (case-insensitive partial), sort theo khoảng cách khi truyền latitude/longitude,
        /// sort theo tên A→Z khi không truyền location.
        /// [Role: Public — không yêu cầu đăng nhập.]
        /// </summary>
        /// <param name="boardgameId">Mã định danh board game (GameTemplates.Id).</param>
        /// <param name="query">Filter tùy chọn: latitude, longitude, name, pageNumber, pageSize.</param>
        /// <response code="200">Danh sách quán cafe có board game đang hoạt động (phân trang, shape NearbyCafeDto).</response>
        /// <response code="404">Không tìm thấy board game hoặc game đã bị vô hiệu hóa.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("{boardgameId:guid}/active-cafes")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(PaginatedResponse<NearbyCafeDto>), 200)]
        public async Task<IActionResult> GetActiveCafesByBoardGame(
            Guid boardgameId,
            [FromQuery] ActiveCafesByBoardGameQueryDto query)
        {
            var result = await _boardGameService.GetActiveCafesByBoardGameAsync(boardgameId, query);
            return NewResponse(200, ApiSuccessMessages.BoardGame.ActiveCafesRetrieved, result);
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

        /// <summary>
        /// Proxy ảnh thumbnail từ nguồn ngoài (BoardGameGeek CDN) về server-side để bypass CORS
        /// cho Flutter Web (CanvasKit renderer taint canvas khi upstream không trả
        /// <c>Access-Control-Allow-Origin</c>).
        /// Mobile (Android/iOS) load ảnh trực tiếp từ upstream — gọi endpoint này vẫn hoạt động nhưng tốn thêm 1 round-trip.
        /// Whitelist host hiện tại: <c>cf.geekdo-images.com</c>, <c>cf.geekdo.com</c>,
        /// <c>images.boardgamegeek.com</c>, <c>boardgamegeek.com</c>. [Role: Public — không cần đăng nhập.]
        /// </summary>
        /// <param name="url">URL ảnh gốc (vd. <c>https://cf.geekdo-images.com/.../pic123.png</c>). Phải là http/https.</param>
        /// <response code="200">Trả về ảnh binary (Content-Type theo upstream, vd <c>image/png</c>, <c>image/jpeg</c>) kèm <c>Cache-Control: public, max-age=86400</c> (24h).</response>
        /// <response code="400">URL rỗng hoặc không đúng định dạng http/https.</response>
        /// <response code="502">Upstream lỗi / timeout / trả non-image / vượt size cap / host không nằm trong whitelist.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("thumbnail-proxy")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 502)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetThumbnailProxy([FromQuery] string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return NewResponse(400, ApiErrorMessages.BoardGame.ThumbnailUrlInvalid, null);
            }

            var result = await _thumbnailProxy.FetchAsync(url);
            if (result == null)
            {
                // Service đã log lý do cụ thể (host / timeout / non-2xx / non-image / oversize).
                // Trả generic 502 cho client — ops team xem log để debug.
                return NewResponse(502, ApiErrorMessages.BoardGame.ThumbnailProxyFailed, null);
            }

            // Cache 24h trên client + CDN. Thumbnail BGG ít khi thay đổi.
            Response.Headers["Cache-Control"] = "public, max-age=86400";
            return new FileStreamResult(result.Value.Stream, result.Value.ContentType)
            {
                EnableRangeProcessing = false
            };
        }
    }
}
