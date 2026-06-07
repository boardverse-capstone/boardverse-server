using BoardVerse.Core.DTOs.Common;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BoardVerse.API.Controllers
{
    [ApiController]
    public abstract class BaseApiController : ControllerBase
    {
        protected IActionResult NewResponse(int statusCode, string message, object? data)
        {
            var response = new ApiResponse
            {
                StatusCode = statusCode,
                Message = message,
                Data = data,
                Timestamp = DateTime.UtcNow,
                Path = Request?.Path.Value ?? string.Empty
            };

            return StatusCode(statusCode, response);
        }

        protected Guid GetUserIdFromClaims()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !Guid.TryParse(idClaim, out var userId))
            {
                throw new UnauthorizedException("Invalid user identifier in token");
            }

            return userId;
        }
    }
}