using BoardVerse.Core.DTOs.Game;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/v1/master-games")]
    [Authorize]
    public class MasterGameController : BaseApiController
    {
        private readonly IGameTemplateService _gameTemplateService;

        public MasterGameController(IGameTemplateService gameTemplateService)
        {
            _gameTemplateService = gameTemplateService;
        }

        /// <summary>
        /// Get paginated list of master board games with optional search filter.
        /// </summary>
        /// <param name="searchTerm">Optional search term to filter games by name.</param>
        /// <param name="pageNumber">Page number (default: 1).</param>
        /// <param name="pageSize">Page size (default: 10).</param>
        /// <response code="200">Returns paginated list of board games.</response>
        /// <response code="401">Unauthorized - missing or invalid token.</response>
        [HttpGet]
        public async Task<IActionResult> GetMasterGames(
            [FromQuery] string? searchTerm,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var query = new GetMasterGamesQuery
            {
                SearchTerm = searchTerm,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            var result = await _gameTemplateService.GetMasterGamesAsync(query);
            return Ok(result);
        }
    }
}
