using System.Net;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Bgg;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Services.Extensions
{
    public static class BggServiceExtensions
    {
        public static IServiceCollection AddBoardVerseBgg(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<BggSettings>(configuration.GetSection(BggSettings.SectionName));

            services.AddHttpClient(BggApiClient.HttpClientNameValue, (sp, client) =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BggSettings>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, settings.RequestTimeoutSeconds));
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());
            });

            services.AddScoped<BggApiClient>();
            services.AddScoped<IBggGameService, BggGameService>();
            return services;
        }
    }
}
