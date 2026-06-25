using BoardVerse.Core.DTOs.Bgg;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/bgg")]
    [Authorize(Roles = "Admin")]
    public class BggController : BaseApiController
    {
        private readonly IBggGameService _bggGameService;

        public BggController(IBggGameService bggGameService)
        {
            _bggGameService = bggGameService;
        }

        /// <summary>
        /// Danh mục chuẩn cấu phần board game (thẻ bài, xúc xắc, meeple, …). [Role: Admin]
        /// </summary>
        /// <response code="200">Trả về danh sách loại linh kiện chuẩn (kind, tên EN/VI, mô tả, số lượng mặc định gợi ý).</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải Admin hoặc tài khoản bị chặn.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("component-catalog")]
        public async Task<IActionResult> GetComponentCatalog()
        {
            var result = await _bggGameService.GetComponentCatalogAsync();
            return this.NewResponse(200, ApiSuccessMessages.Bgg.ComponentCatalogRetrieved, result);
        }

        /// <summary>
        /// Tìm game trên BoardGameGeek (xmlapi2/search). [Role: Admin — chỉ quản trị hệ thống mới thêm game vào master catalog.]
        /// </summary>
        /// <param name="query">Từ khóa tìm kiếm (tối thiểu 2 ký tự, không phân biệt hoa/thường).</param>
        /// <response code="200">Danh sách kết quả khớp (bggId, name, yearPublished); có thể rỗng nếu BGG không trả match.</response>
        /// <response code="400">Query rỗng hoặc ngắn hơn 2 ký tự.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải Admin hoặc tài khoản bị chặn.</response>
        /// <response code="500">BGG API không phản hồi hoặc lỗi hệ thống không mong đợi.</response>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query)
        {
            var result = await _bggGameService.SearchGamesAsync(query);
            return this.NewResponse(200, ApiSuccessMessages.Bgg.SearchCompleted, result);
        }

        /// <summary>
        /// Xem trước metadata + linh kiện trước khi import vào master catalog. [Role: Admin]
        /// </summary>
        /// <param name="bggId">Mã game trên BoardGameGeek (số nguyên dương, vd. Catan = 13).</param>
        /// <param name="curatedComponentsOnly">Khi true, chỉ trả linh kiện từ GameCatalog nội bộ; không suy luận từ mechanics BGG.</param>
        /// <response code="200">Metadata BGG, categories/mechanics và danh sách linh kiện đã resolve (curated hoặc inferred).</response>
        /// <response code="400">bggId không phải số nguyên dương.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải Admin hoặc tài khoản bị chặn.</response>
        /// <response code="404">BGG không trả game tương ứng hoặc không parse được response.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpGet("games/{bggId:int}")]
        public async Task<IActionResult> PreviewGame(
            int bggId,
            [FromQuery] bool curatedComponentsOnly = false)
        {
            var result = await _bggGameService.GetGamePreviewAsync(bggId, curatedComponentsOnly);
            return this.NewResponse(200, ApiSuccessMessages.Bgg.PreviewRetrieved, result);
        }

        /// <summary>
        /// Import game từ BGG vào GameTemplates (danh mục master toàn hệ thống). [Role: Admin]
        /// </summary>
        /// <param name="request">bggId (bắt buộc), overwriteExisting (ghi đè nếu đã có theo BggId hoặc tên), curatedComponentsOnly (chỉ linh kiện curated).</param>
        /// <response code="201">Tạo GameTemplate mới từ BGG thành công.</response>
        /// <response code="200">Cập nhật GameTemplate đã tồn tại khi overwriteExisting=true.</response>
        /// <response code="400">bggId không hợp lệ hoặc không resolve được linh kiện để import.</response>
        /// <response code="401">Thiếu token, token hết hạn hoặc token không hợp lệ.</response>
        /// <response code="403">Không phải Admin hoặc tài khoản bị chặn.</response>
        /// <response code="404">BGG không trả game tương ứng hoặc không parse được response.</response>
        /// <response code="409">Game đã tồn tại và overwriteExisting=false.</response>
        /// <response code="500">Lỗi hệ thống không mong đợi.</response>
        [HttpPost("import")]
        public async Task<IActionResult> ImportGame([FromBody] ImportGameFromBggRequestDto request)
        {
            var result = await _bggGameService.ImportGameAsync(request);
            var status = result.Created ? 201 : 200;
            var message = result.Created
                ? ApiSuccessMessages.Bgg.GameImported
                : ApiSuccessMessages.Bgg.GameUpdated;
            return this.NewResponse(status, message, result);
        }
    }
}
