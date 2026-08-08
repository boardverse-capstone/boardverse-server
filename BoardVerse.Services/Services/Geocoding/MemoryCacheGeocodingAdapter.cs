using Microsoft.Extensions.Caching.Memory;

namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// Adapter dùng <c>Microsoft.Extensions.Caching.Memory.IMemoryCache</c> (đã có sẵn trong
    /// <c>builder.Services.AddMemoryCache()</c> qua <c>AddBoardVerseRedis</c> fallback).
    /// </summary>
    public sealed class MemoryCacheGeocodingAdapter : IMemoryCacheGeocoding
    {
        private readonly IMemoryCache _cache;

        public MemoryCacheGeocodingAdapter(IMemoryCache cache)
        {
            _cache = cache;
        }

        public bool TryGetValue<T>(string key, out T? value)
        {
            if (_cache.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public void Set<T>(string key, T value, TimeSpan ttl)
        {
            _cache.Set(key, value, ttl);
        }
    }
}