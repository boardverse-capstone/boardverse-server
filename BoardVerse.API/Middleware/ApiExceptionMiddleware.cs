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

                    context.Response.ContentType = "application/json; charset=utf-8";
                    var payload = JsonSerializer.Serialize(response, jsonOptions);
                    await context.Response.WriteAsync(payload);
                }
            }
            catch (AppException ex)
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.StatusCode = ex.StatusCode;

                object? data = null;

                // GAP-5 Fix: Khi không đủ BVC, gửi thêm thông tin top-up guidance.
                if (ex is InsufficientBvcBalanceException bvcEx)
                {
                    data = new
                    {
                        currentBalance = bvcEx.CurrentBalance,
                        requiredBalance = bvcEx.RequiredBalance,
                        missingAmount = bvcEx.MissingAmount,
                        topUpRequired = true,
                        topUpAction = "POST /api/v1/wallet/topup"
                    };
                }
                // BR-EXCEPTION-4 fix (2026-10-02): Khi ghép lobby vượt sức chứa ghế của quán,
                // gửi kèm numbers (targetActiveMembers, sourceActiveMembers, targetSeatCapacity,
                // combinedCount) để client/UI hiển thị thông báo cụ thể + suggest action.
                else if (ex is InsufficientSeatsForMergeException seatsEx)
                {
                    data = new
                    {
                        targetActiveMembers = seatsEx.TargetActiveMembers,
                        sourceActiveMembers = seatsEx.SourceActiveMembers,
                        targetSeatCapacity = seatsEx.TargetSeatCapacity,
                        combinedCount = seatsEx.CombinedCount,
                        exceedBy = seatsEx.CombinedCount - seatsEx.TargetSeatCapacity
                    };
                }

                var response = new ApiResponse
                {
                    StatusCode = ex.StatusCode,
                    Message = ex.Message,
                    Data = data,
                    ErrorCode = ex.ErrorCode,
                    Timestamp = DateTime.UtcNow,
                    Path = context.Request.Path.Value ?? string.Empty
                };

                var payload = JsonSerializer.Serialize(response, jsonOptions);
                await context.Response.WriteAsync(payload);
            }
            catch (InvalidOperationException ex)
            {
                // InvalidOperationException is treated as an internal error because business
                // validation errors MUST be expressed via AppException subclasses (NotFoundException,
                // ConflictException, ForbiddenException, BadRequestException, InternalServerErrorException).
                // Any code still throwing InvalidOperationException is a bug and should be logged so it can
                // be migrated; do NOT leak message content or regex-match it for a status code.
                //
                // Sinh traceId ngắn (8 hex) để log và response cùng chia sẻ 1 mã — admin tra log theo
                // traceId sẽ thấy ngay exception gốc mà không lộ message nội bộ ra client.
                var traceId = Guid.NewGuid().ToString("N")[..8];
                _logger.LogError(ex,
                    "Unexpected InvalidOperationException (should be AppException). " +
                    "TraceId={TraceId} | Path={Path}",
                    traceId, context.Request.Path);

                var response = new ApiResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Message = ApiErrorMessages.System.InternalLogicError(traceId),
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Path = context.Request.Path.Value ?? string.Empty
                };

                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.StatusCode = response.StatusCode;
                var payload = JsonSerializer.Serialize(response, jsonOptions);
                await context.Response.WriteAsync(payload);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client đã ngắt request (đóng tab, refresh, navigation, timeout). Đây KHÔNG phải lỗi
                // server — chỉ là EF Core/đã thấy CancellationToken bị hủy khi client mất kết nối.
                // Log ở mức Information để tránh noise; không ghi body vì response đã bị abort.
                _logger.LogInformation(
                    "Request cancelled by client. Path={Path}",
                    context.Request.Path);
                // Không ghi response — client không còn đọc được.
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

                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.StatusCode = response.StatusCode;
                var payload = JsonSerializer.Serialize(response, jsonOptions);
                await context.Response.WriteAsync(payload);
            }
        }
    }
}