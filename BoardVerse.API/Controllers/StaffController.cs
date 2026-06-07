using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    [Route("api/staff")]
    [Authorize(Roles = "CafeStaff")]
    public class StaffController : BaseApiController
    {
        private readonly ICafeService _cafeService;

        public StaffController(ICafeService cafeService)
        {
            _cafeService = cafeService;
        }

        /// <summary>
        /// Get list of cafes where the current staff member works.
        /// </summary>
        /// <response code="200">Workplaces retrieved successfully.</response>
        /// <response code="401">Unauthorized - missing or invalid token.</response>
        [HttpGet("my-cafes")]
        public async Task<IActionResult> GetMyWorkplaces()
        {
            var currentStaffId = GetUserIdFromClaims();

            var result = await _cafeService.GetMyWorkplacesAsync(currentStaffId);
            return this.NewResponse(200, "Workplaces retrieved successfully", result);
        }
    }
}
