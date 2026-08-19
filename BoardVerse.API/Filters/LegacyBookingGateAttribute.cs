using BoardVerse.Core.Messages;
using BoardVerse.Core.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace BoardVerse.API.Filters;

/// <summary>
/// Gate filter cho legacy Booking endpoints.
/// Khi <c>LegacyBookingSettings.Enabled = false</c>, mọi action trong
/// controller gắn attribute này trả <c>410 Gone</c> với header
/// <c>Deprecation: true</c> (RFC 8594).
///
/// Implement <see cref="IAsyncResourceFilter"/> (không phải ActionFilter) để
/// short-circuit trước cả <c>AuthorizationFilter</c> — khi gate off, không
/// cần parse JWT, query user, hay load claim. Tiết kiệm CPU/DB cho deprecation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class LegacyBookingGateAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var settings = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<LegacyBookingSettings>>().Value;

        if (settings.Enabled)
        {
            await next();
            return;
        }

        // Gated off — trả 410 Gone với Deprecation headers (RFC 8594).
        var headers = context.HttpContext.Response.Headers;
        headers["Deprecation"] = "true";
        headers["Sunset"] = "Wed, 31 Dec 2026 23:59:59 GMT";
        headers["Link"] = "</docs/api/booking#deprecation>; rel=\"deprecation\"";

        context.Result = new ObjectResult(new
        {
            status = 410,
            message = ApiErrorMessages.Booking.LegacyEndpointDisabled,
            migration = ApiErrorMessages.Booking.LegacyEndpointMigrationPath
        })
        {
            StatusCode = StatusCodes.Status410Gone
        };

        // Không gọi next() — không chạy tiếp auth filter / action.
        await Task.CompletedTask;
    }
}
