using BoardVerse.Core.DTOs.Discovery;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

[ApiController]
[Route("api/v1/discovery")]
[Authorize]
public class BoardGameDiscoveryController : BaseApiController
{
    private readonly IBoardGameDiscoveryService _discoveryService;
    private readonly IBoardGameService _boardGameService;

    public BoardGameDiscoveryController(
        IBoardGameDiscoveryService discoveryService,
        IBoardGameService boardGameService)
    {
        _discoveryService = discoveryService;
        _boardGameService = boardGameService;
    }

    /// <summary>
    /// Lấy danh sách thể loại board game dùng cho bộ lọc khảo sát. [Role: Public — không cần đăng nhập.]
    /// </summary>
    /// <response code="200">Trả về danh sách thể loại (id, name, slug, sortOrder).</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("categories")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCategories()
    {
        var result = await _boardGameService.GetCategoriesAsync();
        return NewResponse(200, ApiSuccessMessages.BoardGame.CategoriesRetrieved, result);
    }

    /// <summary>
    /// Khảo sát gợi ý board game theo tiêu chí của player (số người, thể loại, thời gian, kinh nghiệm).
    /// Trả về danh sách game có MatchScore, kèm quán cafe gần nhất và lobby đang mở (nếu có location).
    /// [Role: Public — không yêu cầu đăng nhập; nếu có token sẽ ưu tiên gợi ý theo vị trí người dùng.]
    /// </summary>
    /// <param name="request">Tiêu chí khảo sát: playerCount (1-5), categoryIds, preferredDurations, experienceLevel, searchKeyword.</param>
    /// <param name="latitude">Vĩ độ player (WGS84, optional — để tìm quán cafe gần nhất).</param>
    /// <param name="longitude">Kinh độ player (WGS84, optional).</param>
    /// <response code="200">Kết quả khảo sát gồm: danh sách game với MatchScore, tổng số kết quả, bộ lọc đã áp dụng.</response>
    /// <response code="400">Số người chơi không hợp lệ (phải 1-5).</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("survey")]
    [AllowAnonymous]
    public async Task<IActionResult> RunSurvey(
        [FromBody] BoardGameSurveyRequestDto request,
        [FromQuery] double? latitude,
        [FromQuery] double? longitude)
    {
        var (userId, _) = GetOptionalViewerContext();

        var result = await _discoveryService.RunSurveyAsync(
            request,
            userId,
            latitude,
            longitude);

        return NewResponse(200, ApiSuccessMessages.Discovery.SurveyCompleted, result);
    }

    /// <summary>
    /// Lấy danh sách board game đã lưu bởi player. [Role: Player — đã đăng nhập]
    /// </summary>
    /// <response code="200">Danh sách board game đã lưu, kèm thông tin game và thời điểm lưu.</response>
    /// <response code="401">Thiếu token hoặc token hết hạn.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedGames()
    {
        var userId = GetUserIdFromClaims();
        var result = await _discoveryService.GetSavedGamesAsync(userId);
        return NewResponse(200, ApiSuccessMessages.Discovery.SavedGamesRetrieved, result);
    }

    /// <summary>
    /// Lưu hoặc bỏ lưu một board game (toggle). Nếu chưa lưu thì lưu, nếu đã lưu thì bỏ lưu. [Role: Player — đã đăng nhập]
    /// </summary>
    /// <param name="gameTemplateId">Mã định danh board game cần lưu hoặc bỏ lưu.</param>
    /// <response code="200">Thao tác thành công, trả về trạng thái IsSaved và thời điểm lưu.</response>
    /// <response code="401">Thiếu token hoặc token hết hạn.</response>
    /// <response code="404">Board game không tồn tại hoặc đã bị vô hiệu hóa.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpPost("saved/{gameTemplateId:guid}")]
    public async Task<IActionResult> ToggleSave(Guid gameTemplateId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _discoveryService.ToggleSaveAsync(userId, gameTemplateId);

        var message = result.IsSaved
            ? ApiSuccessMessages.Discovery.GameSaved
            : ApiSuccessMessages.Discovery.GameUnsaved;

        return NewResponse(200, message, result);
    }

    /// <summary>
    /// Xóa một board game đã lưu khỏi danh sách của player. [Role: Player — đã đăng nhập]
    /// </summary>
    /// <param name="gameTemplateId">Mã định danh board game đã lưu cần xóa.</param>
    /// <response code="200">Đã xóa bản lưu thành công.</response>
    /// <response code="401">Thiếu token hoặc token hết hạn.</response>
    /// <response code="404">Board game không tồn tại trong danh sách đã lưu.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpDelete("saved/{gameTemplateId:guid}")]
    public async Task<IActionResult> UnsaveGame(Guid gameTemplateId)
    {
        var userId = GetUserIdFromClaims();
        var result = await _discoveryService.UnsaveGameAsync(userId, gameTemplateId);

        return NewResponse(200, ApiSuccessMessages.Discovery.GameUnsaved, result);
    }
}
