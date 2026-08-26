using BoardVerse.Core.Data;
using BoardVerse.Core.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Helpers
{
    /// <summary>
    /// Helper bật/tắt nhanh các ràng buộc thời gian (check-in window, lobby deadline,
    /// cancel grace, refund milestones, no-show grace, walk-in window, tournament start).
    /// Thứ tự ưu tiên: HTTP header > Query param > System config DB.
    /// </summary>
    public static class TimeWindowGuard
    {
        public const string HeaderName = "X-Bypass-Time-Window";
        public const string QueryName = "bypassTimeWindow";

        public static async Task<bool> ShouldBypassAsync(
            HttpContext? httpContext,
            ISystemConfigurationProvider? configProvider,
            ILogger logger,
            string operation,
            Guid? entityId = null,
            CancellationToken ct = default)
        {
            // Lớp 1: HTTP header (per-request, cao nhất)
            if (TryParseOverride(GetHeaderValue(httpContext, HeaderName), out var headerValue))
            {
                if (headerValue)
                {
                    LogBypass(logger, operation, entityId, "Header");
                }
                return headerValue;
            }

            // Lớp 2: Query param (per-request)
            if (TryParseOverride(GetQueryValue(httpContext, QueryName), out var queryValue))
            {
                if (queryValue)
                {
                    LogBypass(logger, operation, entityId, "Query");
                }
                return queryValue;
            }

            // Lớp 3: System config DB (global, có thể toggle qua admin endpoint)
            if (configProvider == null)
            {
                return false;
            }
            var configValue = await configProvider.GetStringAsync(
                SystemConfigKeys.BypassTimeWindowValidations, "false");
            if (bool.TryParse(configValue, out var dbValue) && dbValue)
            {
                LogBypass(logger, operation, entityId, "DB");
                return true;
            }

            return false;
        }

        public static Task<bool> ShouldBypassAsync(
            ISystemConfigurationProvider? configProvider,
            ILogger logger,
            string operation,
            Guid? entityId = null,
            CancellationToken ct = default)
        {
            return ShouldBypassAsync(null, configProvider, logger, operation, entityId, ct);
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

        private static bool TryParseOverride(string? raw, out bool value)
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
                "[TimeWindowGuard] BYPASS time-window check | operation={Operation} | entityId={EntityId} | source={Source}",
                operation, entityId ?? Guid.Empty, source);
        }
    }
}