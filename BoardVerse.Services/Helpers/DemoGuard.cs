using BoardVerse.Core.Data;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Helpers
{
    /// <summary>
    /// Helper bật/tắt nhanh các BR-DEMO-* (demo mode override). Khi DB toggle
    /// <see cref="SystemConfigKeys.DemoLoosenLobbyConstraints"/> = true,
    /// các ràng buộc BR-USER-LIMIT-01/04/05 + BR-LOBBY-01a/b + BR-NEW-05 +
    /// BR-CHECKIN-01 sẽ bị skip để demo happy case chạy mượt.
    ///
    /// Thứ tự ưu tiên: HTTP header > Query param > SystemConfig DB.
    /// Chỉ bật trên Neon testing branch, KHÔNG bật production.
    /// </summary>
    public static class DemoGuard
    {
        public const string HeaderName = "X-Bypass-Demo-Locks";
        public const string QueryName = "bypassDemoLocks";

        public static async Task<bool> ShouldBypassDemoLocksAsync(
            HttpContext? httpContext,
            ISystemConfigurationProvider? configProvider,
            ILogger? logger,
            string operation,
            Guid? entityId = null,
            CancellationToken ct = default)
        {
            // Lớp 1: HTTP header (per-request, cao nhất)
            var headerValue = GetHeaderValue(httpContext, HeaderName);
            if (TryParse(headerValue, out var hv))
            {
                if (hv) LogBypass(logger, operation, entityId, "Header");
                return hv;
            }

            // Lớp 2: Query param (per-request)
            var queryValue = GetQueryValue(httpContext, QueryName);
            if (TryParse(queryValue, out var qv))
            {
                if (qv) LogBypass(logger, operation, entityId, "Query");
                return qv;
            }

            // Lớp 3: SystemConfig DB (global toggle)
            if (configProvider == null)
            {
                return false;
            }
            var dbValue = await configProvider.GetStringAsync(
                SystemConfigKeys.DemoLoosenLobbyConstraints, "false");
            if (bool.TryParse(dbValue, out var db) && db)
            {
                LogBypass(logger, operation, entityId, "DB");
                return true;
            }

            return false;
        }

        public static Task<bool> ShouldBypassDemoLocksAsync(
            ISystemConfigurationProvider? configProvider,
            ILogger? logger,
            string operation,
            Guid? entityId = null,
            CancellationToken ct = default)
        {
            return ShouldBypassDemoLocksAsync(null, configProvider, logger, operation, entityId, ct);
        }

        private static string? GetHeaderValue(HttpContext? ctx, string name)
        {
            if (ctx?.Request?.Headers == null) return null;
            return ctx.Request.Headers.TryGetValue(name, out var v) ? v.ToString() : null;
        }

        private static string? GetQueryValue(HttpContext? ctx, string name)
        {
            if (ctx?.Request?.Query == null) return null;
            return ctx.Request.Query.TryGetValue(name, out var v) ? v.ToString() : null;
        }

        private static bool TryParse(string? raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var trimmed = raw.Trim();
            if (bool.TryParse(trimmed, out value)) return true;
            if (trimmed == "1") { value = true; return true; }
            if (trimmed == "0") { value = false; return true; }
            return false;
        }

        private static void LogBypass(ILogger? logger, string operation, Guid? entityId, string source)
        {
            if (logger == null) return;
            logger.LogWarning(
                "[DemoGuard] BYPASS demo-locks check | operation={Operation} | entityId={EntityId} | source={Source}",
                operation, entityId ?? Guid.Empty, source);
        }
    }
}
