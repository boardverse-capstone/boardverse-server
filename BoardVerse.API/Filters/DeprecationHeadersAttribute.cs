using Microsoft.AspNetCore.Mvc.Filters;

namespace BoardVerse.API.Filters;

/// <summary>
/// Phase 4 / RFC 8594: Tự động thêm Deprecation response headers cho các endpoint
/// legacy đang trong quá trình migrate sang Reservation flow.
///
/// Headers theo RFC 8594 (The "Deprecation" HTTP Header Field):
/// <list type="bullet">
///   <item><description><c>Deprecation: true</c> — endpoint deprecated.</description></item>
///   <item><description><c>Sunset: &lt;RFC 7231 IMF-fixdate&gt;</c> — ngày xóa.</description></item>
///   <item><description><c>Link: &lt;url&gt;; rel="deprecation"</c> — link tới docs.</description></item>
/// </list>
///
/// Áp dụng attribute ở controller hoặc action method:
/// <code>
/// [DeprecationHeaders(Sunset = "Wed, 31 Dec 2026 23:59:59 GMT",
///                     DocsLink = "/docs/api/booking#deprecation")]
/// </code>
///
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DeprecationHeadersAttribute : Attribute, IResultFilter
{
    /// <summary>
    /// RFC 7231 IMF-fixdate format (vd: "Wed, 31 Dec 2026 23:59:59 GMT").
    /// Ngày mà endpoint sẽ bị xóa / đổi sang trả 410 Gone.
    /// </summary>
    public string Sunset { get; set; } = "Wed, 31 Dec 2026 23:59:59 GMT";

    /// <summary>URL tới docs mô tả endpoint thay thế (relative hoặc absolute).</summary>
    public string DocsLink { get; set; } = "/docs/api/booking#deprecation";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;
        headers["Deprecation"] = "true";
        headers["Sunset"] = Sunset;
        headers["Link"] = $"<{DocsLink}>; rel=\"deprecation\"";
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
        // No-op: chỉ set ở executing để đảm bảo luôn có dù response sau có ghi đè headers khác.
    }
}