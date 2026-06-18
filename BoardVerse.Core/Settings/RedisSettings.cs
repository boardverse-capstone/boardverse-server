namespace BoardVerse.Core.Settings
{
    public class RedisSettings
    {
        public const string SectionName = "Redis";

        /// <summary>StackExchange.Redis connection string or redis:// URL. Leave empty for local in-memory cache.</summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>Key prefix for all cache entries (multi-tenant / shared Redis).</summary>
        public string InstanceName { get; set; } = "BoardVerse:";
    }
}
