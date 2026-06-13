using System.Net;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Services.Extensions
{
    public static class EmailServiceExtensions
    {
        public static IServiceCollection AddBoardVerseEmail(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<BrevoSettings>(configuration.GetSection(BrevoSettings.SectionName));

            services.AddHttpClient(nameof(BrevoEmailService), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(OutboundEmailHttpHandlerFactory.Create);

            services.AddScoped<IEmailService, BrevoEmailService>();
            return services;
        }
    }
}
