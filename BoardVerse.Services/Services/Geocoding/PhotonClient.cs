using System.Globalization;
using System.Text.Json;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// Fallback geocoding client dùng Photon (Komoot OSM mirror).
    /// Endpoint: <c>GET https://photon.komoot.io/reverse?lon={lng}&amp;lat={lat}</c>.
    /// Public, free, no User-Agent required, thường accessible từ môi trường
    /// Render Free (không bị block egress như Nominatim public server).
    ///
    /// Photon response shape (khác Nominatim):
    /// <code>
    /// { "type":"Feature", "geometry":{...},
    ///   "properties": { "osm_id":..., "osm_type":..., "name":"...",
    ///     "city":"TP. Hồ Chí Minh", "district":"Quận 1",
    ///     "country":"Việt Nam", "countrycode":"vn", ... } }
    /// </code>
    /// </summary>
    public sealed class PhotonClient : IGeocodingClient
    {
        public const string HttpClientNameValue = "BoardVerse.Photon";

        private const string PhotonBaseUrl = "https://photon.komoot.io";
        private const string PhotonReversePath = "/reverse";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PhotonClient> _logger;

        public PhotonClient(
            IHttpClientFactory httpClientFactory,
            IOptions<NominatimSettings> settings,
            ILogger<PhotonClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<string?> ReverseGeocodeRawAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
        {
            var url = BuildReverseUrl(latitude, longitude);
            var client = _httpClientFactory.CreateClient(HttpClientNameValue);

            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Photon reverse failed (lat={Lat}, lng={Lng}): HTTP {Status}",
                        latitude,
                        longitude,
                        (int)response.StatusCode);
                    return null;
                }

                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (
                ex is HttpRequestException ||
                ex is TaskCanceledException ||
                ex is OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Photon reverse call failed (lat={Lat}, lng={Lng})",
                    latitude,
                    longitude);
                return null;
            }
        }

        /// <summary>
        /// Parse Photon JSON. Hỗ trợ 2 shape:
        /// 1. FeatureCollection (standard Photon reverse): <c>{ "features": [ { "properties": { ... } } ] }</c>
        /// 2. Single Feature (một số proxy): <c>{ "properties": { ... } }</c>
        /// </summary>
        public static ReverseGeocodeResult? ParsePhoton(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                JsonElement properties;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("properties", out var directProperties)
                    && directProperties.ValueKind == JsonValueKind.Object)
                {
                    properties = directProperties;
                }
                else if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("features", out var featuresElement)
                    && featuresElement.ValueKind == JsonValueKind.Array
                    && featuresElement.GetArrayLength() > 0
                    && featuresElement[0].ValueKind == JsonValueKind.Object
                    && featuresElement[0].TryGetProperty("properties", out var featureProperties)
                    && featureProperties.ValueKind == JsonValueKind.Object)
                {
                    properties = featureProperties;
                }
                else
                {
                    return null;
                }

                string? Get(params string[] keys)
                {
                    foreach (var key in keys)
                    {
                        if (properties.TryGetProperty(key, out var v)
                            && v.ValueKind == JsonValueKind.String)
                        {
                            var s = v.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                            {
                                return s;
                            }
                        }
                    }
                    return null;
                }

                // Photon field mapping (đa ngôn ngữ tuỳ region):
                //   district: "Quận 1" / "District 1"
                //   county:   "Quận Bình Thạnh" / "Landkreis"
                //   city:     "TP. Hồ Chí Minh" / "Ho Chi Minh City"
                //   state:    "Hồ Chí Minh" (province level)
                //   country:  "Việt Nam" / "Vietnam"
                var district = Get("district", "county");
                var city = Get("city");
                var state = Get("state");
                var country = Get("country");
                var name = Get("name");

                // Fallback: Photon 1 số vùng chỉ trả `name` thay vì city/district.
                if (string.IsNullOrWhiteSpace(district) && !string.IsNullOrWhiteSpace(city))
                {
                    district = null; // không tự suy ra
                }

                var computedCity = city ?? state;

                var parts = new[] { district, computedCity, country }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!.Trim())
                    .ToList();
                var displayName = parts.Count > 0
                    ? string.Join(", ", parts)
                    : name;

                return new ReverseGeocodeResult
                {
                    District = district,
                    City = computedCity,
                    Country = country,
                    DisplayName = displayName,
                    OsmType = Get("osm_type"),
                    Importance = null
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string BuildReverseUrl(double latitude, double longitude)
        {
            var lat = latitude.ToString("F7", CultureInfo.InvariantCulture);
            var lng = longitude.ToString("F7", CultureInfo.InvariantCulture);
            return $"{PhotonBaseUrl}{PhotonReversePath}?lon={lng}&lat={lat}";
        }
    }
}