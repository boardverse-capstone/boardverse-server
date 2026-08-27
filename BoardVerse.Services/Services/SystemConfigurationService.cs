using System.Globalization;
using System.Text.Json;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
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
        private readonly BoardVerseDbContext _db;

        public SystemConfigurationService(
            ISystemConfigurationRepository repository,
            IDistributedCache cache,
            BoardVerseDbContext db)
        {
            _repository = repository;
            _cache = cache;
            _db = db;
        }

        public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default)
        {
            var raw = await GetStringAsync(key, fallback.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        public async Task<double> GetDoubleAsync(string key, double fallback, CancellationToken cancellationToken = default)
        {
            var raw = await GetStringAsync(key, fallback.ToString(CultureInfo.InvariantCulture));
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default)
        {
            var raw = await GetStringAsync(key, fallback ? "true" : "false");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            var trimmed = raw.Trim();
            return trimmed.ToLowerInvariant() switch
            {
                "true" => true,
                "false" => false,
                "1" => true,
                "0" => false,
                "yes" => true,
                "no" => false,
                "on" => true,
                "off" => false,
                var s when bool.TryParse(s, out var parsed) => parsed,
                _ => fallback
            };
        }

        public async Task<string> GetStringAsync(string key, string fallback, CancellationToken cancellationToken = default)
        {
            var map = await GetConfigMapAsync();
            return map.TryGetValue(key, out var value) ? value : fallback;
        }

        public Task InvalidateCacheAsync(CancellationToken cancellationToken = default) => _cache.RemoveAsync(CacheKey, cancellationToken);

        public async Task<IReadOnlyList<SystemConfigEntryDto>> GetAllConfigsAsync(CancellationToken cancellationToken = default)
        {
            var configs = await _repository.GetAllAsync();
            return configs.Select(Map).ToList();
        }

        public async Task<IReadOnlyList<SystemConfigEntryDto>> BulkUpdateConfigsAsync(
            SystemConfigBulkUpdateRequestDto request, CancellationToken cancellationToken = default)
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

            // GAP-R4-A9 Fix: Wrap Upsert + SaveChanges + InvalidateCache trong 1 transaction.
// 2 admin cùng bulk-update → SaveChanges của admin A chưa commit thì admin B đọc cache cũ
// (cache TTL 10s) → race. Mặc dù cache TTL 10s sẽ tự expire, việc commit trong transaction
// đảm bảo invalidation xảy ra trước khi admin B thấy data mới.
        var ambientTx = _db.Database.CurrentTransaction;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? ownedTx = null;
        if (ambientTx == null)
        {
            ownedTx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, default);
        }

        try
        {
            await _repository.UpsertAsync(updates);
            await _repository.SaveChangesAsync();
            await InvalidateCacheAsync();
            if (ownedTx != null)
            {
                await ownedTx.CommitAsync(default);
            }
        }
        catch
        {
            if (ownedTx != null)
            {
                await ownedTx.RollbackAsync(default);
            }
            throw;
        }
        finally
        {
            if (ownedTx != null)
            {
                await ownedTx.DisposeAsync();
            }
        }

        return await GetAllConfigsAsync();
    }

        public async Task<SystemConfigEntryDto> SetConfigValueAsync(string key, string value, CancellationToken cancellationToken = default)
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

        public async Task<bool> IsBypassTimeWindowEnabledAsync(CancellationToken cancellationToken = default)
            => await GetBoolAsync(SystemConfigKeys.BypassTimeWindowValidations, fallback: false);

        public async Task<bool> IsDemoLoosenLobbyConstraintsEnabledAsync(CancellationToken cancellationToken = default)
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
