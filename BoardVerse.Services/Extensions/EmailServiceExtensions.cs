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
            services.Configure<MailjetSettings>(configuration.GetSection(MailjetSettings.SectionName));

            services.AddHttpClient(nameof(MailjetEmailService), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(MailjetHttpHandlerFactory.Create);

            services.AddScoped<IEmailService, MailjetEmailService>();

            return services;
        }
    }
}
