using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BoardVerse.API.Controllers
{
    /// <summary>
    /// Base class cho MỌI API controller trong BoardVerse.
    /// Mặc định [Authorize] — controller con phải gắn [AllowAnonymous] nếu muốn public.
    /// Lý do: trước đó dùng FallbackPolicy ở Program.cs line 139, nhưng nó chặn cả public
    /// controller (BoardGame, Cafe, MasterGame, Health...) gây 401. Fix = đảo logic: BaseApiController
    /// đã [Authorize] → tự động áp dụng, controller con chỉ cần [AllowAnonymous] nếu muốn public.
    /// </summary>
    [ApiController]
    [Authorize]
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
                throw new UnauthorizedException(ApiErrorMessages.Controller.InvalidUserIdClaim);
            }

            return userId;
        }

        protected (Guid? UserId, string? Role) GetOptionalViewerContext()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return (null, null);
            }

            Guid? userId = null;
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(idClaim) && Guid.TryParse(idClaim, out var parsed))
            {
                userId = parsed;
            }

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            return (userId, role);
        }
    }
}