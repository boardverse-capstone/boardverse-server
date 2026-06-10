using System.Net;
using System.Net.Mail;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Email
{
    public class SmtpEmailService : IEmailService
    {
        private readonly SmtpSettings _settings;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IOptions<SmtpSettings> settings, ILogger<SmtpEmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            _logger.LogInformation(
                "SMTP configured — Host: {Host}, Port: {Port}, EnableSsl: {EnableSsl}",
                _settings.Host,
                _settings.Port,
                _settings.EnableSsl);
        }

        public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            try
            {
                using var client = new SmtpClient(_settings.Host, _settings.Port)
                {
                    EnableSsl = _settings.EnableSsl,
                    UseDefaultCredentials = false,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                if (!string.IsNullOrWhiteSpace(_settings.Username))
                {
                    client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
                }

                _logger.LogInformation("Sending email via SMTP to {To} ({Host}:{Port})", to, _settings.Host, _settings.Port);

                using var mail = new MailMessage(_settings.From, to, subject, body) { IsBodyHtml = isHtml };
                await client.SendMailAsync(mail);

                _logger.LogInformation("Email sent via SMTP to {To}", to);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "SMTP send failed. StatusCode: {StatusCode}", ex.StatusCode);
                throw new EmailSendingException(
                    $"SMTP send failed for host '{_settings.Host}' on port {_settings.Port}. " +
                    $"StatusCode: {ex.StatusCode}. Check username, password, EnableSsl, and From address.",
                    ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending email via SMTP");
                throw new EmailSendingException($"Unexpected error sending email: {ex.Message}", ex);
            }
        }
    }
}
