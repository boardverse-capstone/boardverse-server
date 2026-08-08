namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// Wrapper mỏng quanh <c>IMemoryCache</c> để <see cref="PlayerGeocodingService"/> dễ unit test
    /// (không phải mock static hoặc sealed Microsoft type).
    /// </summary>
    public interface IMemoryCacheGeocoding
    {
        bool TryGetValue<T>(string key, out T? value);
        void Set<T>(string key, T value, TimeSpan ttl);
    }
}