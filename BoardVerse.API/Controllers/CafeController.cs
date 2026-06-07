using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/cafes")]
    [Authorize(Roles = "Manager")]
    public class CafeController : BaseApiController
    {
        private readonly ICafeService _cafeService;

        public CafeController(ICafeService cafeService)
        {
            _cafeService = cafeService;
        }

        /// <summary>
        /// Add a staff member to a cafe.
        /// </summary>
        /// <param name="cafeId">The ID of the cafe.</param>
        /// <param name="dto">Staff member information (Email, FullName).</param>
        /// <response code="200">Staff member added successfully.</response>
        /// <response code="400">Invalid request data.</response>
        /// <response code="401">Unauthorized - missing or invalid token.</response>
        /// <response code="403">Forbidden - user is not the manager of this cafe.</response>
        /// <response code="404">Cafe not found.</response>
        /// <response code="409">Staff member already exists in this cafe.</response>
        [HttpPost("{cafeId:guid}/staff")]
        public async Task<IActionResult> AddStaff(Guid cafeId, [FromBody] AddStaffRequestDto dto)
        {
            // Extract current manager ID from JWT claims
            var currentManagerIdStr = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentManagerIdStr) || !Guid.TryParse(currentManagerIdStr, out var currentManagerId))
            {
                return this.NewResponse(401, "Invalid user identifier in token", null);
            }

            await _cafeService.AddStaffAsync(cafeId, currentManagerId, dto);
            return this.NewResponse(200, "Staff member added successfully", null);
        }

        /// <summary>
        /// Get paginated list of staff members for a cafe.
        /// </summary>
        /// <param name="cafeId">The ID of the cafe.</param>
        /// <param name="pageNumber">Page number (default: 1).</param>
        /// <param name="pageSize">Page size (default: 10, max: 100).</param>
        /// <response code="200">Staff list retrieved successfully.</response>
        /// <response code="401">Unauthorized - missing or invalid token.</response>
        /// <response code="403">Forbidden - user is not the manager of this cafe.</response>
        /// <response code="404">Cafe not found.</response>
        [HttpGet("{cafeId:guid}/staff")]
        public async Task<IActionResult> GetStaffList(Guid cafeId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            // Extract current manager ID from JWT claims
            var currentManagerIdStr = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentManagerIdStr) || !Guid.TryParse(currentManagerIdStr, out var currentManagerId))
            {
                return this.NewResponse(401, "Invalid user identifier in token", null);
            }

            var paginationParams = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await _cafeService.GetStaffListAsync(cafeId, currentManagerId, paginationParams);
            return this.NewResponse(200, "Staff list retrieved successfully", result);
        }

        /// <summary>
        /// Remove a staff member from a cafe.
        /// </summary>
        /// <param name="cafeId">The ID of the cafe.</param>
        /// <param name="staffId">The ID of the staff member to remove.</param>
        /// <response code="200">Staff member removed successfully.</response>
        /// <response code="401">Unauthorized - missing or invalid token.</response>
        /// <response code="403">Forbidden - user is not the manager of this cafe.</response>
        /// <response code="404">Cafe or staff member not found.</response>
        [HttpDelete("{cafeId:guid}/staff/{staffId:guid}")]
        public async Task<IActionResult> RemoveStaff(Guid cafeId, Guid staffId)
        {
            // Extract current manager ID from JWT claims
            var currentManagerIdStr = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentManagerIdStr) || !Guid.TryParse(currentManagerIdStr, out var currentManagerId))
            {
                return this.NewResponse(401, "Invalid user identifier in token", null);
            }

            await _cafeService.RemoveStaffAsync(cafeId, currentManagerId, staffId);
            return this.NewResponse(200, "Staff member removed successfully", null);
        }
    }
}
