using System.Globalization;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// Composite geocoding client: thử <see cref="IGeocodingClient"/> primary trước
    /// (mặc định Nominatim), nếu trả null hoặc throw → fallback <see cref="PhotonClient"/>.
    ///
    /// Mục đích: trên Render Free egress bị chặn với Nominatim public server
    /// nhưng Photon (photon.komoot.io) thường accessible. Fallback đảm bảo
    /// PlayerLocationDto vẫn có district/city/country khi Nominatim fail.
    /// </summary>
    public sealed class FallbackGeocodingClient : IGeocodingClient
    {
        private const string PhotonSourceMarker = "photon";
        private const string NominatimSourceMarker = "nominatim";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly NominatimSettings _settings;
        private readonly ILogger<FallbackGeocodingClient> _logger;

        public FallbackGeocodingClient(
            IHttpClientFactory httpClientFactory,
            IOptions<NominatimSettings> settings,
            ILogger<FallbackGeocodingClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<string?> ReverseGeocodeRawAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
        {
            // Primary: Photon (photon.komoot.io) — không có hard cap 1 req/s, accessible từ Render Free.
            // Trước đây Nominatim làm primary nhưng bị rate-limit nặng ở production do
            // (a) cache quantization quá nhỏ → miss liên tục, và
            // (b) Render Free egress thỉnh thoảng bị Nominatim block.
            var photonRaw = await TryCallAsync(PhotonClient.HttpClientNameValue, async client =>
            {
                var url = $"https://photon.komoot.io/reverse?lon={longitude.ToString("F7", CultureInfo.InvariantCulture)}&lat={latitude.ToString("F7", CultureInfo.InvariantCulture)}";
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Primary geocoder (Photon) returned HTTP {Status} for ({Lat}, {Lng})",
                        (int)response.StatusCode,
                        latitude,
                        longitude);
                    return null;
                }
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }, cancellationToken, latitude, longitude);

            if (!string.IsNullOrWhiteSpace(photonRaw))
            {
                // Trả về JSON wrapper với marker để PlayerGeocodingService route tới Photon parser.
                return "{\"_source\":\"" + PhotonSourceMarker + "\"," + photonRaw.TrimStart('{');
            }

            // Fallback: Nominatim. Dùng rate tier cho phép nhưng cache miss nặng vẫn dẫn tới 429.
            _logger.LogInformation(
                "Primary geocoder (Photon) failed for ({Lat}, {Lng}); falling back to Nominatim",
                latitude,
                longitude);

            var nominatimRaw = await TryCallAsync(NominatimClient.HttpClientNameValue, async client =>
            {
                var url = BuildNominatimUrl(latitude, longitude);
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Fallback geocoder (Nominatim) returned HTTP {Status} for ({Lat}, {Lng})",
                        (int)response.StatusCode,
                        latitude,
                        longitude);
                    return null;
                }
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }, cancellationToken, latitude, longitude);

            if (string.IsNullOrWhiteSpace(nominatimRaw))
            {
                return null;
            }

            // Trả về JSON wrapper với marker để PlayerGeocodingService route tới Nominatim parser.
            return "{\"_source\":\"" + NominatimSourceMarker + "\"," + nominatimRaw.TrimStart('{');
        }

        private async Task<string?> TryCallAsync(
            string httpClientName,
            Func<HttpClient, Task<string?>> action,
            CancellationToken cancellationToken,
            double latitude = 0,
            double longitude = 0)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(httpClientName);
                return await action(client);
            }
            catch (Exception ex) when (
                ex is HttpRequestException ||
                ex is TaskCanceledException ||
                ex is OperationCanceledException ||
                ex is InvalidOperationException)
            {
                // GAP-R4-A6 Fix: truyền lat/lng thật vào log để SRE debug được production errors
                // (trước đây hardcode 0,0 → không reproduce được user report).
                _logger.LogWarning(
                    ex,
                    "Geocoder {ClientName} threw for ({Lat}, {Lng})",
                    httpClientName,
                    latitude,
                    longitude);
                return null;
            }
        }

        private string BuildNominatimUrl(double latitude, double longitude)
        {
            var baseUrl = _settings.ApiBaseUrl.TrimEnd('/');
            var path = _settings.ReversePath.TrimStart('/');
            var lat = latitude.ToString("F7", CultureInfo.InvariantCulture);
            var lng = longitude.ToString("F7", CultureInfo.InvariantCulture);
            var lang = Uri.EscapeDataString(_settings.AcceptLanguage ?? "vi");
            return $"{baseUrl}/{path}?lat={lat}&lon={lng}&format=jsonv2&addressdetails=1&accept-language={lang}&zoom=18";
        }
    }
}