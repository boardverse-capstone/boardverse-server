using System.Globalization;
using System.Text.Json;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Caching.Distributed;

namespace BoardVerse.Services.Services
{
    public class SystemConfigurationService : ISystemConfigurationProvider, IAdminSystemConfigurationService
    {
        private const string CacheKey = "boardverse:system-config:all";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ISystemConfigurationRepository _repository;
        private readonly IDistributedCache _cache;

        public SystemConfigurationService(
            ISystemConfigurationRepository repository,
            IDistributedCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public async Task<int> GetIntAsync(string key, int fallback)
        {
            var raw = await GetStringAsync(key, fallback.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        public async Task<double> GetDoubleAsync(string key, double fallback)
        {
            var raw = await GetStringAsync(key, fallback.ToString(CultureInfo.InvariantCulture));
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        public async Task<string> GetStringAsync(string key, string fallback)
        {
            var map = await GetConfigMapAsync();
            return map.TryGetValue(key, out var value) ? value : fallback;
        }

        public Task InvalidateCacheAsync() => _cache.RemoveAsync(CacheKey);

        public async Task<IReadOnlyList<SystemConfigEntryDto>> GetAllConfigsAsync()
        {
            var configs = await _repository.GetAllAsync();
            return configs.Select(Map).ToList();
        }

        public async Task<IReadOnlyList<SystemConfigEntryDto>> BulkUpdateConfigsAsync(
            SystemConfigBulkUpdateRequestDto request)
        {
            var utcNow = DateTime.UtcNow;
            var updates = request.Configs
                .Select(item =>
                {
                    SystemConfigKeys.SeedDefaults.TryGetValue(item.ConfigKey, out var seed);
                    return new SystemConfiguration
                    {
                        ConfigKey = item.ConfigKey.Trim(),
                        ConfigValue = item.ConfigValue.Trim(),
                        Description = seed.Description ?? string.Empty,
                        UpdatedAt = utcNow
                    };
                })
                .ToList();

            await _repository.UpsertAsync(updates);
            await _repository.SaveChangesAsync();
            await InvalidateCacheAsync();

            return await GetAllConfigsAsync();
        }

        private async Task<IReadOnlyDictionary<string, string>> GetConfigMapAsync()
        {
            var cached = await _cache.GetStringAsync(CacheKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                var fromCache = JsonSerializer.Deserialize<Dictionary<string, string>>(cached, JsonOptions);
                if (fromCache != null)
                {
                    return fromCache;
                }
            }

            var configs = await _repository.GetAllAsync();
            var map = configs.ToDictionary(c => c.ConfigKey, c => c.ConfigValue, StringComparer.OrdinalIgnoreCase);

            await _cache.SetStringAsync(
                CacheKey,
                JsonSerializer.Serialize(map, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration });

            return map;
        }

        private static SystemConfigEntryDto Map(SystemConfiguration config) => new()
        {
            ConfigKey = config.ConfigKey,
            ConfigValue = config.ConfigValue,
            Description = config.Description,
            UpdatedAt = config.UpdatedAt
        };
    }
}
