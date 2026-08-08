using System.Globalization;
using System.Net;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Geocoding
{
    /// <summary>
    /// HTTP client cho OpenStreetMap Nominatim API.
    /// Tuân thủ Nominatim usage policy:
    /// - User-Agent header bắt buộc (không được mặc định).
    /// - Rate limit 1 req/s (không cần enforce ở code vì server tự reject).
    /// - Endpoint: <c>GET {baseUrl}/reverse?lat={lat}&lon={lon}&format=jsonv2&amp;addressdetails=1&amp;accept-language={lang}</c>.
    /// </summary>
    public sealed class NominatimClient : IGeocodingClient
    {
        public const string HttpClientNameValue = "BoardVerse.Nominatim";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly NominatimSettings _settings;
        private readonly ILogger<NominatimClient> _logger;

        public NominatimClient(
            IHttpClientFactory httpClientFactory,
            IOptions<NominatimSettings> settings,
            ILogger<NominatimClient> logger)
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
            var url = BuildReverseUrl(latitude, longitude);
            var client = _httpClientFactory.CreateClient(HttpClientNameValue);

            for (var attempt = 1; attempt <= _settings.MaxRetryAttempts; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(url, cancellationToken);

                    if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning(
                            "Nominatim reverse failed (lat={Lat}, lng={Lng}) attempt {Attempt}/{Max}: HTTP {Status}",
                            latitude,
                            longitude,
                            attempt,
                            _settings.MaxRetryAttempts,
                            (int)response.StatusCode);

                        if (attempt < _settings.MaxRetryAttempts)
                        {
                            await Task.Delay(_settings.RetryDelayMilliseconds, cancellationToken);
                            continue;
                        }

                        return null;
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        // 404 nghĩa là Nominatim không tìm thấy địa điểm — không retry.
                        return null;
                    }

                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        ex,
                        "Nominatim reverse timed out (lat={Lat}, lng={Lng}) attempt {Attempt}/{Max}",
                        latitude,
                        longitude,
                        attempt,
                        _settings.MaxRetryAttempts);

                    if (attempt < _settings.MaxRetryAttempts)
                    {
                        await Task.Delay(_settings.RetryDelayMilliseconds, cancellationToken);
                        continue;
                    }

                    return null;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Nominatim reverse HTTP error (lat={Lat}, lng={Lng}) attempt {Attempt}/{Max}",
                        latitude,
                        longitude,
                        attempt,
                        _settings.MaxRetryAttempts);

                    if (attempt < _settings.MaxRetryAttempts)
                    {
                        await Task.Delay(_settings.RetryDelayMilliseconds, cancellationToken);
                        continue;
                    }

                    return null;
                }
            }

            return null;
        }

        private string BuildReverseUrl(double latitude, double longitude)
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