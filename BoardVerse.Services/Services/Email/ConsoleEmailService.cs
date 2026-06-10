using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services.Email
{
    /// <summary>
    /// Local development — logs email content instead of sending.
    /// </summary>
    public class ConsoleEmailService : IEmailService
    {
        private readonly ILogger<ConsoleEmailService> _logger;

        public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            _logger.LogWarning(
                "[ConsoleEmail] Provider=Console — email NOT sent. To={To}, Subject={Subject}, Body={Body}",
                to,
                subject,
                body);

            return Task.CompletedTask;
        }
    }
}
