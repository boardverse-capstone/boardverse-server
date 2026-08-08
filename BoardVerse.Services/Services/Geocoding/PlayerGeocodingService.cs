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

        /// <summary>Marker value lưu trong cache khi cả primary + fallback đều fail.
        /// Check đầu method để skip cả 2 call trong vòng <see cref="NegativeCacheTtl"/>.
        /// Tránh spam Nominatim/Photon khi network hoặc upstream rate-limit.</summary>
        private const string NegativeCacheMarker = "__rate_limited__";

        /// <summary>TTL cho negative cache. 5 phút — đủ để rate-limit của Nominatim reset (cap 1 req/s).</summary>
        private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(5);

        public async Task<ReverseGeocodeResult?> ReverseGeocodeAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = BuildCacheKey(latitude, longitude, _settings.CoordinateQuantization);

            if (_settings.EnableCache)
            {
                // Check positive cache trước (kết quả thành công).
                if (_cache.TryGetValue(cacheKey, out ReverseGeocodeResult? cached))
                {
                    return cached;
                }

                // Check negative cache (cả 2 client vừa fail gần đây) → skip call luôn.
                if (_cache.TryGetValue(cacheKey, out string? marker) && marker == NegativeCacheMarker)
                {
                    _logger.LogDebug(
                        "Negative cache hit for ({Lat}, {Lng}); skipping geocoder call.",
                        latitude,
                        longitude);
                    return null;
                }
            }

            string? raw;
            try
            {
                raw = await _client.ReverseGeocodeRawAsync(latitude, longitude, cancellationToken);
            }
            catch (Exception ex)
            {
                // Cả 2 client throw → cache marker 5 phút để không gọi lại liên tục.
                WriteNegativeCache(cacheKey);
                _logger.LogWarning(
                    ex,
                    "Reverse geocode call failed at service level (lat={Lat}, lng={Lng}). Returning null.",
                    latitude,
                    longitude);
                return null;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                // Cả 2 client trả null (HTTP non-2xx hết) → cache marker 5 phút.
                WriteNegativeCache(cacheKey);
                return null;
            }

            var parsed = ParseAnyResponse(raw);

            // Only cache successful (non-null) results.
            // Caching null would poison the cache for 30 days, causing all subsequent
            // requests at the same quantized coordinate to skip the geocoder call forever.
            if (parsed is not null && _settings.EnableCache)
            {
                var ttl = TimeSpan.FromHours(Math.Max(1, _settings.CacheTtlHours));
                _cache.Set(cacheKey, parsed, ttl);
            }

            return parsed;
        }

        private void WriteNegativeCache(string cacheKey)
        {
            if (!_settings.EnableCache)
            {
                return;
            }
            try
            {
                _cache.Set(cacheKey, NegativeCacheMarker, NegativeCacheTtl);
            }
            catch (Exception ex)
            {
                // Cache ghi fail → bỏ qua, không ảnh hưởng request.
                _logger.LogDebug(ex, "Failed to write negative cache for key {Key}", cacheKey);
            }
        }

        /// <summary>
        /// Route response tới parser phù hợp dựa trên wrapper <c>"_source":"photon"</c>
        /// hoặc <c>"_source":"nominatim"</c> do <see cref="FallbackGeocodingClient"/> chèn vào.
        /// Trước đây chỉ detect photon; sau khi swap Photon làm primary, Nominatim
        /// là response của fallback nên cần detect thêm marker.
        /// </summary>
        private static ReverseGeocodeResult? ParseAnyResponse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // Photon trả FeatureCollection — wrapper {"_source":"photon", "features":[...]}
            if (raw.Contains("\"_source\":\"photon\"", StringComparison.Ordinal))
            {
                return StripWrapperAndParse(raw, "photon", PhotonClient.ParsePhoton);
            }

            // Nominatim trả object đơn (jsonv2) — wrapper {"_source":"nominatim", <nominatim-json>}
            if (raw.Contains("\"_source\":\"nominatim\"", StringComparison.Ordinal))
            {
                return StripWrapperAndParse(raw, "nominatim", NominatimResponseParser.Parse);
            }

            // Fallback: response thô (không có wrapper) — thử Nominatim parser trước.
            return NominatimResponseParser.Parse(raw);
        }

        /// <summary>
        /// Strip wrapper của FallbackGeocodingClient (key "_source" + giá trị marker),
        /// trả JSON gốc về parser tương ứng. An toàn với mọi vị trí key trong response.
        /// </summary>
        private static ReverseGeocodeResult? StripWrapperAndParse(
            string raw,
            string marker,
            Func<string, ReverseGeocodeResult?> parser)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    return null;
                }

                using var ms = new System.IO.MemoryStream();
                using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
                {
                    writer.WriteStartObject();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.NameEquals("_source"))
                        {
                            continue;
                        }
                        prop.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                ms.Position = 0;
                using var innerDoc = System.Text.Json.JsonDocument.Parse(ms);
                var innerJson = innerDoc.RootElement.GetRawText();
                return parser(innerJson);
            }
            catch (System.Text.Json.JsonException)
            {
                // Regex fallback nếu JSON parse fail: cắt tay từ marker tới cuối.
                var markerStr = "\"_source\":\"" + marker + "\"";
                var idx = raw.IndexOf(markerStr, StringComparison.Ordinal);
                if (idx > 0)
                {
                    var commaIdx = raw.IndexOf(',', idx);
                    if (commaIdx > 0)
                    {
                        var inner = raw[(commaIdx + 1)..].TrimStart();
                        return parser(inner);
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Build cache key với toạ độ quantized (mặc định 0.00001 ≈ 1.1m).
        /// Tránh trường hợp GPS nhảy vài mét → mỗi request thành cache miss.
        ///
        /// IMPORTANT: quantization có thể override qua parameter (vd. 0.001 ≈ 100m cho production
        /// để tăng cache hit rate). Caller PHẢI truyền cùng quantization với mọi request
        /// cho cùng 1 vùng địa lý.
        /// </summary>
        public static string BuildCacheKey(
            double latitude,
            double longitude,
            double quantization = 0.00001)
        {
            var qLat = Math.Round(latitude / quantization) * quantization;
            var qLng = Math.Round(longitude / quantization) * quantization;

            // Format precision theo quantization: 0.00001 → F5, 0.001 → F3.
            // Tránh hash collision giữa các quantization khác nhau.
            var decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(quantization)));
            var fmt = "F" + decimals;

            return $"{CacheKeyPrefix}{qLat.ToString(fmt, CultureInfo.InvariantCulture)}:{qLng.ToString(fmt, CultureInfo.InvariantCulture)}";
        }
    }
}