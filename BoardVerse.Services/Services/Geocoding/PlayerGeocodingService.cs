using System.Globalization;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Geocoding
{
    public class PlayerGeocodingService : IPlayerGeocodingService
    {
        /// <summary>Cache key prefix — dùng chung cho cả player location (cache theo quantized lat/lng).</summary>
        public const string CacheKeyPrefix = "geocode:reverse:";

        private readonly IGeocodingClient _client;
        private readonly NominatimSettings _settings;
        private readonly IMemoryCacheGeocoding _cache;
        private readonly ILogger<PlayerGeocodingService> _logger;

        public PlayerGeocodingService(
            IGeocodingClient client,
            IOptions<NominatimSettings> settings,
            IMemoryCacheGeocoding cache,
            ILogger<PlayerGeocodingService> logger)
        {
            _client = client;
            _settings = settings.Value;
            _cache = cache;
            _logger = logger;
        }

        public async Task<ReverseGeocodeResult?> ReverseGeocodeAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = BuildCacheKey(latitude, longitude);

            if (_settings.EnableCache && _cache.TryGetValue(cacheKey, out ReverseGeocodeResult? cached))
            {
                return cached;
            }

            string? raw;
            try
            {
                raw = await _client.ReverseGeocodeRawAsync(latitude, longitude, cancellationToken);
            }
            catch (Exception ex)
            {
                // Nominatim fail → return null thay vì throw, để PlayerLocationDto fallback về lat/lng only.
                _logger.LogWarning(
                    ex,
                    "Reverse geocode call failed at service level (lat={Lat}, lng={Lng}). Returning null.",
                    latitude,
                    longitude);
                return null;
            }

            var parsed = ParseAnyResponse(raw);

            // Only cache successful (non-null) results.
            // Caching null would poison the cache for 30 days, causing all subsequent
            // requests at the same quantized coordinate to skip the Nominatim call forever.
            if (parsed is not null && _settings.EnableCache)
            {
                var ttl = TimeSpan.FromHours(Math.Max(1, _settings.CacheTtlHours));
                _cache.Set(cacheKey, parsed, ttl);
            }

            return parsed;
        }

        /// <summary>
        /// Route response tới parser phù hợp dựa trên wrapper <c>"_source":"photon"</c>
        /// do <see cref="FallbackGeocodingClient"/> chèn vào khi dùng Photon fallback.
        /// </summary>
        private static ReverseGeocodeResult? ParseAnyResponse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // FallbackGeocodingClient trả wrapper {"_source":"photon", "features":[...]}
            // → nhận diện bằng field "_source" ngay đầu object.
            if (raw.Contains("\"_source\":\"photon\"", StringComparison.Ordinal))
            {
                // Strip wrapper — chỉ giữ JSON gốc của Photon để PhotonClient.ParsePhoton xử lý.
                var innerStart = raw.IndexOf('{', raw.IndexOf("\"_source\"", StringComparison.Ordinal));
                if (innerStart > 0)
                {
                    var inner = raw[innerStart..];
                    return PhotonClient.ParsePhoton(inner);
                }
                return null;
            }

            return NominatimResponseParser.Parse(raw);
        }

        /// <summary>
        /// Build cache key với toạ độ quantized (mặc định 0.00001 ≈ 1.1m).
        /// Tránh trường hợp GPS nhảy vài mét → mỗi request thành cache miss.
        /// </summary>
        public static string BuildCacheKey(double latitude, double longitude, double quantization = 0.00001)
        {
            var qLat = Math.Round(latitude / quantization) * quantization;
            var qLng = Math.Round(longitude / quantization) * quantization;
            return $"{CacheKeyPrefix}{qLat.ToString("F5", CultureInfo.InvariantCulture)}:{qLng.ToString("F5", CultureInfo.InvariantCulture)}";
        }
    }
}