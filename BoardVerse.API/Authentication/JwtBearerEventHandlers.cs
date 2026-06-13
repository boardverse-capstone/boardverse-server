using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.IRepositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace BoardVerse.API.Authentication
{
    internal static class JwtBearerEventHandlers
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        };

        public static JwtBearerEvents Create() => new()
        {
            OnTokenValidated = OnTokenValidatedAsync,
            OnAuthenticationFailed = OnAuthenticationFailedAsync,
            OnChallenge = OnChallengeAsync,
            OnForbidden = OnForbiddenAsync,
        };

        private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
        {
            var userRepository = context.HttpContext.RequestServices.GetRequiredService<IAuthRepository>();
            var token = context.SecurityToken as JwtSecurityToken;
            if (token != null)
            {
                var raw = new JwtSecurityTokenHandler().WriteToken(token);
                if (await userRepository.IsTokenBlacklistedAsync(raw))
                {
                    Fail(context, StatusCodes.Status401Unauthorized,
                        "Access token has been revoked. Please sign in again.");
                    return;
                }
            }

            var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userId, out var parsedUserId))
            {
                Fail(context, StatusCodes.Status401Unauthorized,
                    "Access token is missing a valid user identifier. Please sign in again.");
                return;
            }

            var user = await userRepository.GetByIdAsync(parsedUserId);
            if (user == null)
            {
                Fail(context, StatusCodes.Status401Unauthorized,
                    "User account no longer exists. Please sign in again.");
                return;
            }

            if (user.IsBlocked)
            {
                var reason = string.IsNullOrWhiteSpace(user.BlockReason)
                    ? "Your account has been blocked."
                    : $"Your account has been blocked. Reason: {user.BlockReason}";
                Fail(context, StatusCodes.Status403Forbidden, reason);
                return;
            }

            if (!user.IsActive)
            {
                Fail(context, StatusCodes.Status403Forbidden,
                    "Your account is deactivated. Contact support to reactivate your account.");
                return;
            }
        }

        private static Task OnAuthenticationFailedAsync(AuthenticationFailedContext context)
        {
            var message = context.Exception switch
            {
                SecurityTokenExpiredException =>
                    "Access token has expired. Use POST /api/auth/refresh-token or sign in again.",
                SecurityTokenInvalidSignatureException =>
                    "Access token signature is invalid. Please sign in again.",
                SecurityTokenException =>
                    "Access token is invalid or malformed. Please sign in again.",
                _ =>
                    "Authentication failed. Please sign in again."
            };

            JwtAuthFailureContext.Set(context.HttpContext, StatusCodes.Status401Unauthorized, message);
            return Task.CompletedTask;
        }

        private static async Task OnChallengeAsync(JwtBearerChallengeContext context)
        {
            context.HandleResponse();

            if (context.Response.HasStarted)
            {
                return;
            }

            var statusCode = JwtAuthFailureContext.GetStatusCode(context.HttpContext)
                ?? StatusCodes.Status401Unauthorized;
            var message = JwtAuthFailureContext.GetMessage(context.HttpContext)
                ?? ResolveChallengeMessage(context);

            await WriteJsonResponseAsync(context.HttpContext, statusCode, message, context.Request.Path);
        }

        private static async Task OnForbiddenAsync(ForbiddenContext context)
        {
            if (context.Response.HasStarted)
            {
                return;
            }

            var message = JwtAuthFailureContext.GetMessage(context.HttpContext)
                ?? "Access denied. Your account does not have the required role or permission for this endpoint.";

            await WriteJsonResponseAsync(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                message,
                context.Request.Path);
        }

        private static void Fail(TokenValidatedContext context, int statusCode, string message)
        {
            JwtAuthFailureContext.Set(context.HttpContext, statusCode, message);
            context.Fail(message);
        }

        private static string ResolveChallengeMessage(JwtBearerChallengeContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.ErrorDescription))
            {
                return context.ErrorDescription;
            }

            var hasBearerHeader = context.Request.Headers.Authorization
                .ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

            if (!hasBearerHeader)
            {
                return "Authorization header is missing. Provide a Bearer access token.";
            }

            return context.Error switch
            {
                "invalid_token" => "Access token is invalid. Please sign in again.",
                "expired_token" => "Access token has expired. Use POST /api/auth/refresh-token or sign in again.",
                _ => "Authentication failed. Please sign in again."
            };
        }

        private static async Task WriteJsonResponseAsync(
            HttpContext httpContext,
            int statusCode,
            string message,
            PathString path)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(new ApiResponse
            {
                StatusCode = statusCode,
                Message = message,
                Data = null,
                Timestamp = DateTime.UtcNow,
                Path = path
            }, JsonOptions);

            await httpContext.Response.WriteAsync(payload);
            JwtAuthFailureContext.MarkResponseWritten(httpContext);
        }
    }
}
