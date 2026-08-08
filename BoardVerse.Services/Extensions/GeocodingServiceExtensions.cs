using System.Net;
using BoardVerse.Core.Settings;
using BoardVerse.Services.Services.Geocoding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Services.Extensions
{
    /// <summary>
    /// DI registration cho Geocoding (Nominatim OpenStreetMap reverse-geocode).
    /// Bind <c>Geocoding</c> section từ <c>appsettings.json</c>; đăng ký HttpClient với
    /// User-Agent + Accept-Language theo Nominatim usage policy.
    /// </summary>
    public static class GeocodingServiceExtensions
    {
        public static IServiceCollection AddBoardVerseGeocoding(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<NominatimSettings>(configuration.GetSection(NominatimSettings.SectionName));

            services.AddHttpClient(NominatimClient.HttpClientNameValue, (sp, client) =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NominatimSettings>>().Value;

                client.Timeout = TimeSpan.FromSeconds(Math.Max(2, settings.RequestTimeoutSeconds));
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

                if (!string.IsNullOrWhiteSpace(settings.UserAgent))
                {
                    // Nominatim yêu cầu UA identifying (vd: "BoardVerse/1.0 (contact@...)")
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
                }

                if (!string.IsNullOrWhiteSpace(settings.AcceptLanguage))
                {
                    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(settings.AcceptLanguage);
                }
            });

            services.AddSingleton<IMemoryCacheGeocoding, DistributedCacheGeocodingAdapter>();
            services.AddScoped<IGeocodingClient, NominatimClient>();
            services.AddScoped<IPlayerGeocodingService, PlayerGeocodingService>();

            return services;
        }
    }
}