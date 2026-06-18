using BoardVerse.Core.Helpers;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Extensions
{
    public sealed record RedisCacheStartupInfo(
        string Backend,
        string? ConnectionString,
        string? InstanceName);

    public static class RedisServiceExtensions
    {
        public const string RedisBackend = "Redis";
        public const string MemoryBackend = "Memory";

        public static IServiceCollection AddBoardVerseRedis(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<RedisSettings>(configuration.GetSection(RedisSettings.SectionName));

            var redisSection = configuration.GetSection(RedisSettings.SectionName);
            var connectionString = RedisConnectionHelper.TryResolveConnectionString(
                redisSection["ConnectionString"],
                configuration.GetConnectionString("Redis"),
                Environment.GetEnvironmentVariable("REDIS_URL")
                    ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION"));

            var instanceName = redisSection["InstanceName"];
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                instanceName = "BoardVerse:";
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                services.AddDistributedMemoryCache();
                services.AddSingleton(new RedisCacheStartupInfo(MemoryBackend, null, null));
            }
            else
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = connectionString;
                    options.InstanceName = instanceName;
                });

                services.AddSingleton(new RedisCacheStartupInfo(RedisBackend, connectionString, instanceName));
            }

            return services;
        }

        public static void LogRedisCacheStartup(this ILogger logger, RedisCacheStartupInfo info)
        {
            if (info.Backend == MemoryBackend)
            {
                logger.LogInformation(
                    "Distributed cache using in-memory backend (local dev). Set REDIS_URL or Redis:ConnectionString to enable Redis.");
                return;
            }

            var endpoint = MaskConnectionString(info.ConnectionString!);
            logger.LogInformation(
                "Distributed cache using Redis. Endpoint={Endpoint}, InstanceName={InstanceName}",
                endpoint,
                info.InstanceName);
        }

        private static string MaskConnectionString(string connectionString)
        {
            var passwordIndex = connectionString.IndexOf("password=", StringComparison.OrdinalIgnoreCase);
            if (passwordIndex < 0)
            {
                return connectionString;
            }

            var end = connectionString.IndexOf(',', passwordIndex);
            return end < 0
                ? connectionString[..passwordIndex] + "password=***"
                : connectionString[..passwordIndex] + "password=***" + connectionString[end..];
        }
    }
}
