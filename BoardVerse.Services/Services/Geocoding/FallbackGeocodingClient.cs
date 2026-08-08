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
            // Primary: Nominatim. Response chuẩn (jsonv2) — PlayerGeocodingService parse thẳng.
            var primaryRaw = await TryCallAsync(NominatimClient.HttpClientNameValue, async client =>
            {
                var url = BuildNominatimUrl(latitude, longitude);
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Primary geocoder (Nominatim) returned HTTP {Status} for ({Lat}, {Lng})",
                        (int)response.StatusCode,
                        latitude,
                        longitude);
                    return null;
                }
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(primaryRaw))
            {
                return primaryRaw;
            }

            // Fallback: Photon. Response khác format (FeatureCollection) — caller
            // (PlayerGeocodingService) cần parse bằng PhotonClient.ParsePhoton.
            // Trả về wrapper JSON có key "_photon":true để caller route tới parser phù hợp.
            _logger.LogInformation(
                "Primary geocoder failed for ({Lat}, {Lng}); falling back to Photon",
                latitude,
                longitude);

            var fallbackRaw = await TryCallAsync(PhotonClient.HttpClientNameValue, async client =>
            {
                var url = $"https://photon.komoot.io/reverse?lon={longitude.ToString("F7", CultureInfo.InvariantCulture)}&lat={latitude.ToString("F7", CultureInfo.InvariantCulture)}";
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Fallback geocoder (Photon) returned HTTP {Status} for ({Lat}, {Lng})",
                        (int)response.StatusCode,
                        latitude,
                        longitude);
                    return null;
                }
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }, cancellationToken);

            if (string.IsNullOrWhiteSpace(fallbackRaw))
            {
                return null;
            }

            // Trả về JSON wrapper để PlayerGeocodingService biết dùng Photon parser.
            return "{\"_source\":\"photon\"," + fallbackRaw.TrimStart('{');
        }

        private async Task<string?> TryCallAsync(
            string httpClientName,
            Func<HttpClient, Task<string?>> action,
            CancellationToken cancellationToken)
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
                _logger.LogWarning(
                    ex,
                    "Geocoder {ClientName} threw for ({Lat}, {Lng})",
                    httpClientName,
                    0,
                    0); // giữ đơn giản — log đầy đủ qua path trên
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