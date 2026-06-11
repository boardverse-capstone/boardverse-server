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

            var mailjetBaseUrl = configuration
                .GetSection(MailjetSettings.SectionName)
                .GetValue<string>(nameof(MailjetSettings.ApiBaseUrl))
                ?? "https://api.mailjet.com/v3.1";

            services.AddHttpClient(nameof(MailjetEmailService), client =>
            {
                client.BaseAddress = new Uri(mailjetBaseUrl.TrimEnd('/') + "/");
            });
            services.AddScoped<IEmailService, MailjetEmailService>();

            return services;
        }
    }
}
