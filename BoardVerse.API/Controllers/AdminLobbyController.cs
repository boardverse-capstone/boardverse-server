using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers;

/// <summary>
/// Admin endpoints cho Lobby: lấy toàn bộ lobby trong hệ thống với filter tuỳ ý, có phân trang.
/// [Role: Admin]
/// </summary>
[ApiController]
[Route("api/v1/admin/lobbies")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AdminLobbyController : BaseApiController
{
    private readonly ILobbyService _lobbyService;

    public AdminLobbyController(ILobbyService lobbyService)
    {
        _lobbyService = lobbyService;
    }

    /// <summary>
    /// Lấy toàn bộ lobby trong hệ thống (bao gồm mọi status — Open, Closed, TimeoutFailed,
    /// HostCancelled, RejectedByCafe, ExpiredByCafe, Dissolved, v.v.). Hỗ trợ filter theo host,
    /// game, cafe, status (int hoặc enum name), khoảng ngày tạo, có phân trang.
    /// Sắp xếp theo CreatedAt desc (lobby mới nhất trước). [Role: Admin]
    /// </summary>
    /// <param name="hostUserId">Optional: filter theo HostUserId.</param>
    /// <param name="gameTemplateId">Optional: filter theo GameTemplateId.</param>
    /// <param name="cafeId">Optional: filter theo CafeId.</param>
    /// <param name="statuses">
    /// Optional: lọc theo 1 hoặc nhiều LobbyStatus (enum int). Truyền nhiều giá trị bằng cách lặp query
    /// (vd: <c>?statuses=0&amp;statuses=1</c>) hoặc comma-separated (vd: <c>?statuses=0,1</c>). Null = tất cả.
    /// </param>
    /// <param name="fromDate">Optional: chỉ lấy lobby tạo từ thời điểm này trở đi (ISO 8601).</param>
    /// <param name="toDate">Optional: chỉ lấy lobby tạo đến thời điểm này (ISO 8601).</param>
    /// <param name="page">Trang (1-indexed, default 1).</param>
    /// <param name="pageSize">Số lobby / trang (1-200, default 50).</param>
    /// <response code="200">Danh sách lobby đã phân trang cùng TotalCount/Page/PageSize/TotalPages.</response>
    /// <response code="400">statuses/page/pageSize không hợp lệ.</response>
    /// <response code="401">Thiếu token.</response>
    /// <response code="403">Không phải Admin.</response>
    /// <response code="500">Lỗi hệ thống không mong đợi.</response>
    [HttpGet]
    public async Task<IActionResult> GetAllLobbies(
        [FromQuery] Guid? hostUserId,
        [FromQuery] Guid? gameTemplateId,
        [FromQuery] Guid? cafeId,
        [FromQuery] List<int>? statuses,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var request = new GetAllLobbiesRequestDto
        {
            HostUserId = hostUserId,
            GameTemplateId = gameTemplateId,
            CafeId = cafeId,
            Statuses = statuses,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize
        };

        var result = await _lobbyService.GetAllLobbiesAsync(request);
        return NewResponse(200, "Lấy toàn bộ lobby trong hệ thống.", result);
    }
}