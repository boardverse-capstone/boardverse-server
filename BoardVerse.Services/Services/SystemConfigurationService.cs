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
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

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

        public async Task<bool> GetBoolAsync(string key, bool fallback)
        {
            var raw = await GetStringAsync(key, fallback ? "true" : "false");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            var trimmed = raw.Trim();
            return trimmed switch
            {
                var s when s.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
                var s when s.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
                var s when s.Equals("1", StringComparison.Ordinal) => true,
                var s when s.Equals("0", StringComparison.Ordinal) => false,
                var s when s.Equals("yes", StringComparison.OrdinalIgnoreCase) => true,
                var s when s.Equals("no", StringComparison.OrdinalIgnoreCase) => false,
                var s when s.Equals("on", StringComparison.OrdinalIgnoreCase) => true,
                var s when s.Equals("off", StringComparison.OrdinalIgnoreCase) => false,
                _ => bool.TryParse(trimmed, out var parsed) ? parsed : fallback
            };
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

        public async Task<SystemConfigEntryDto> SetConfigValueAsync(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Config key must not be empty.", nameof(key));
            }

            var trimmedKey = key.Trim();
            SystemConfigKeys.SeedDefaults.TryGetValue(trimmedKey, out var seed);

            var entity = new SystemConfiguration
            {
                ConfigKey = trimmedKey,
                ConfigValue = value?.Trim() ?? string.Empty,
                Description = seed.Description ?? string.Empty,
                UpdatedAt = DateTime.UtcNow
            };

            await _repository.UpsertAsync(new List<SystemConfiguration> { entity });
            await _repository.SaveChangesAsync();
            await InvalidateCacheAsync();

            return Map(entity);
        }

        public async Task<bool> IsBypassTimeWindowEnabledAsync()
            => await GetBoolAsync(SystemConfigKeys.BypassTimeWindowValidations, fallback: false);

        public async Task<bool> IsDemoLoosenLobbyConstraintsEnabledAsync()
            => await GetBoolAsync(SystemConfigKeys.DemoLoosenLobbyConstraints, fallback: false);

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
