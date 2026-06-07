using BoardVerse.Services.IServices;
using BoardVerse.Core.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace BoardVerse.Services.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly string _from;
        private readonly bool _enableSsl;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            var smtp = _configuration.GetSection("Smtp");
            _host = smtp["Host"] ?? "localhost";
            _port = int.TryParse(smtp["Port"], out var p) ? p : 25;
            _username = smtp["Username"] ?? string.Empty;
            _password = smtp["Password"] ?? string.Empty;
            _from = smtp["From"] ?? "noreply@boardverse.local";
            _enableSsl = bool.TryParse(smtp["EnableSsl"], out var s) ? s : false;

            _logger.LogInformation("SMTP Configuration - Host: {Host}, Port: {Port}, EnableSsl: {EnableSsl}", 
                _host, _port, _enableSsl);
        }

        public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            try
            {
                using var client = new SmtpClient(_host, _port)
                {
                    EnableSsl = _enableSsl,
                    UseDefaultCredentials = false,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                if (!string.IsNullOrWhiteSpace(_username))
                {
                    client.Credentials = new NetworkCredential(_username, _password);
                }

                _logger.LogInformation("Attempting to send email to {To} via {Host}:{Port}", to, _host, _port);
                var mail = new MailMessage(_from, to, subject, body) { IsBodyHtml = isHtml };
                await client.SendMailAsync(mail);
                _logger.LogInformation("Email sent successfully to {To}", to);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "SMTP send failed. StatusCode: {StatusCode}, Message: {Message}", 
                    ex.StatusCode, ex.Message);
                throw new EmailSendingException($"SMTP send failed for host '{_host}' on port {_port}. StatusCode: {ex.StatusCode}. Check username/password, EnableSsl, and that the Gmail app password matches the SMTP account.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending email");
                throw new EmailSendingException($"Unexpected error sending email: {ex.Message}", ex);
            }
        }
    }
}
