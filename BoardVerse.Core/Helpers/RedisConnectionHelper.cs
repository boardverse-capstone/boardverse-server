namespace BoardVerse.Core.Helpers
{
    public static class RedisConnectionHelper
    {
        /// <summary>
        /// Resolves Redis connection from env/config when explicitly set. Returns null if unset (local dev → in-memory cache).
        /// Supports redis:// and rediss:// URLs.
        /// </summary>
        public static string? TryResolveConnectionString(
            string? configConnectionString,
            string? connectionStringsRedis,
            string? envRedisUrl)
        {
            foreach (var candidate in new[] { envRedisUrl, connectionStringsRedis, configConnectionString })
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                return NormalizeConnectionString(candidate.Trim());
            }

            return null;
        }

        private static string NormalizeConnectionString(string value)
        {
            if (!value.StartsWith("redis://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            var useSsl = value.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);
            var uri = new Uri(value);
            var userInfo = uri.UserInfo.Split(':', 2);
            var password = userInfo.Length == 2 ? Uri.UnescapeDataString(userInfo[1]) : null;
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 6379;

            var parts = new List<string> { $"{host}:{port}" };
            if (!string.IsNullOrWhiteSpace(password))
            {
                parts.Add($"password={password}");
            }

            if (useSsl)
            {
                parts.Add("ssl=True");
            }

            return string.Join(',', parts);
        }
    }
}
