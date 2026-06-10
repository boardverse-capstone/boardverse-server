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
            services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
            services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));

            var provider = configuration
                .GetSection(EmailSettings.SectionName)
                .GetValue<string>(nameof(EmailSettings.Provider)) ?? "Console";

            if (string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
            {
                services.AddScoped<IEmailService, SmtpEmailService>();
            }
            else
            {
                services.AddScoped<IEmailService, ConsoleEmailService>();
            }

            return services;
        }
    }
}
