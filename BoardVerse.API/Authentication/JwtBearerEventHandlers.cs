using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
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

            var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userId, out var parsedUserId))
            {
                Fail(context, StatusCodes.Status401Unauthorized,
                    ApiErrorMessages.Jwt.MissingUserIdentifier);
                return;
            }

            var user = await userRepository.GetByIdAsync(parsedUserId);
            if (user == null)
            {
                Fail(context, StatusCodes.Status401Unauthorized,
                    ApiErrorMessages.Jwt.UserNoLongerExists);
                return;
            }

            var utcNow = DateTime.UtcNow;
            if (UserAccessHelper.TryClearExpiredSuspension(user, utcNow))
            {
                await userRepository.SaveChangesAsync();
            }

            if (UserAccessHelper.IsAccessRestricted(user, utcNow, out var accessMessage))
            {
                Fail(context, StatusCodes.Status403Forbidden, accessMessage);
                return;
            }
        }

        private static Task OnAuthenticationFailedAsync(AuthenticationFailedContext context)
        {
            var message = context.Exception switch
            {
                SecurityTokenExpiredException => ApiErrorMessages.Jwt.TokenExpired,
                SecurityTokenInvalidSignatureException => ApiErrorMessages.Jwt.TokenInvalidSignature,
                SecurityTokenException => ApiErrorMessages.Jwt.TokenInvalid,
                _ => ApiErrorMessages.Jwt.AuthenticationFailed
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
                ?? ApiErrorMessages.Jwt.AccessDenied;

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
                return ApiErrorMessages.Jwt.AuthorizationHeaderMissing;
            }

            return context.Error switch
            {
                "invalid_token" => ApiErrorMessages.Jwt.TokenInvalid,
                "expired_token" => ApiErrorMessages.Jwt.TokenExpired,
                _ => ApiErrorMessages.Jwt.AuthenticationFailed
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
