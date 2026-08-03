using BoardVerse.API.Authentication;
using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoardVerse.API.Middleware
{
    public class ApiExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiExceptionMiddleware> _logger;

        public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            };

            try
            {
                await _next(context);

                if (context.Response.HasStarted || JwtAuthFailureContext.IsResponseWritten(context))
                {
                    return;
                }

                // Handle non-success status codes (like 401/403/404) and return the consistent response shape
                if (context.Response.StatusCode >= 400)
                {
                    var response = new ApiResponse
                    {
                        StatusCode = context.Response.StatusCode,
                        Message = ApiErrorMessages.Http.Fallback(
                            context.Response.StatusCode,
                            context.Request.Path.Value ?? string.Empty),
                        Data = null,
                        Timestamp = DateTime.UtcNow,
                        Path = context.Request.Path.Value ?? string.Empty
                    };

                    context.Response.ContentType = "application/json";
                    var payload = JsonSerializer.Serialize(response, jsonOptions);
                    await context.Response.WriteAsync(payload);
                }
            }
            catch (AppException ex)
            {
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = ex.StatusCode;

                var response = new ApiResponse
                {
                    StatusCode = ex.StatusCode,
                    Message = ex.Message,
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Path = context.Request.Path.Value ?? string.Empty
                };

                var payload = JsonSerializer.Serialize(response, jsonOptions);
                await context.Response.WriteAsync(payload);
            }
            catch (InvalidOperationException ex)
            {
                // Business rule validation errors thrown as InvalidOperationException
                // Determine status code based on message pattern
                var statusCode = GetStatusCodeForBusinessError(ex.Message);
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = statusCode;

                var response = new ApiResponse
                {
                    StatusCode = statusCode,
                    Message = ex.Message,
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Path = context.Request.Path.Value ?? string.Empty
                };

                var payload = JsonSerializer.Serialize(response, jsonOptions);
                await context.Response.WriteAsync(payload);
            }
            catch (Exception ex)
            {
                // Return a generic error message to clients. Detailed exception information is logged server-side.
                _logger.LogError(ex, "An unexpected error occurred while processing request: {Path}", context.Request.Path);

                var response = new ApiResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Message = ApiErrorMessages.Http.Fallback(
                        (int)HttpStatusCode.InternalServerError,
                        context.Request.Path.Value ?? string.Empty),
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Path = context.Request.Path.Value ?? string.Empty
                };

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = response.StatusCode;
                var payload = JsonSerializer.Serialize(response, jsonOptions);
                await context.Response.WriteAsync(payload);
            }
        }

        /// <summary>
        /// Determines appropriate HTTP status code based on business error message pattern.
        /// </summary>
        private static int GetStatusCodeForBusinessError(string message)
        {
            // 403 Forbidden: account status, cooling-off, cross-role violations
            var forbiddenPatterns = new[]
            {
                "suspended",
                "banned",
                "bị giới hạn",
                "cooling-off",
                "thành viên của.*lobby",
                "host của.*lobby"
            };

            foreach (var pattern in forbiddenPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(message, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return 403;
                }
            }

            // 409 Conflict: overlap, already exists, limit exceeded, not available
            var conflictPatterns = new[]
            {
                "overlap",
                "đã có",
                "đã tồn tại",
                "already exists",
                "already has",
                "limit",
                "cap",
                "hết chỗ",
                "hết ghế",
                "không đủ",
                "chưa đủ"
            };

            foreach (var pattern in conflictPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(message, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return 409;
                }
            }

            // Default to 400 Bad Request for other business validation errors
            return 400;
        }
    }
}