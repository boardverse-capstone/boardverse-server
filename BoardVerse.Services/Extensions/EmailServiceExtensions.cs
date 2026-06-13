using System.Net.Security;
using System.Security.Authentication;
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
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }
            });

            services.AddScoped<IEmailService, MailjetEmailService>();

            return services;
        }
    }
}
