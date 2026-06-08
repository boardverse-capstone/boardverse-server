using BoardVerse.Core.DTOs.Common;
using BoardVerse.Core.Exceptions;
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

                // Handle non-success status codes (like 401/403/404) and return the consistent response shape
                if (context.Response.HasStarted) return;

                if (context.Response.StatusCode >= 400)
                {
                    var response = new ApiResponse
                    {
                        StatusCode = context.Response.StatusCode,
                        Message = ReasonPhrase(context.Response.StatusCode),
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
            catch (Exception ex)
            {
                // Return a generic error message to clients. Detailed exception information is logged server-side.
                _logger.LogError(ex, "An unexpected error occurred while processing request: {Path}", context.Request.Path);

                var response = new ApiResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Message = "An unexpected error occurred.",
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

        private static string ReasonPhrase(int statusCode)
        {
            return statusCode switch
            {
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Not Found",
                409 => "Conflict",
                429 => "Too Many Requests",
                500 => "Internal Server Error",
                _ => "Error"
            };
        }
    }
}