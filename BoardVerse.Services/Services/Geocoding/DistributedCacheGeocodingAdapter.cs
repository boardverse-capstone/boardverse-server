using System.Text.Json;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Caching.Distributed;

namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// Adapter dùng <c>IDistributedCache</c> (Redis prod / memory dev) để cache kết quả reverse-geocode.
    /// Cache key theo <see cref="PlayerGeocodingService.BuildCacheKey"/> (quantized lat/lng).
    /// </summary>
    public sealed class DistributedCacheGeocodingAdapter : IMemoryCacheGeocoding
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly IDistributedCache _cache;

        public DistributedCacheGeocodingAdapter(IDistributedCache cache)
        {
            _cache = cache;
        }

        public bool TryGetValue<T>(string key, out T? value)
        {
            try
            {
                var raw = _cache.GetString(key);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    value = default;
                    return false;
                }

                value = JsonSerializer.Deserialize<T>(raw, JsonOpts);
                return value is not null;
            }
            catch (Exception)
            {
                // Cache lỗi → fail open, không block request user.
                value = default;
                return false;
            }
        }

        public void Set<T>(string key, T value, TimeSpan ttl)
        {
            try
            {
                var payload = JsonSerializer.Serialize(value, JsonOpts);
                _cache.SetString(key, payload, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                });
            }
            catch (Exception)
            {
                // Cache lỗi ghi → bỏ qua, request vẫn succeed.
            }
        }
    }
}