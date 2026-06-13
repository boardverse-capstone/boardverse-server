using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Email
{
    public class MailjetEmailService : IEmailService
    {
        private readonly MailjetSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MailjetEmailService> _logger;

        public MailjetEmailService(
            IOptions<MailjetSettings> settings,
            IHttpClientFactory httpClientFactory,
            ILogger<MailjetEmailService> logger)
        {
            _settings = settings.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            _logger.LogInformation(
                "Mailjet configured — ApiBaseUrl: {ApiBaseUrl}, Sender: {SenderName} <{SenderEmail}>",
                _settings.ApiBaseUrl,
                _settings.SenderName,
                _settings.SenderEmail);
        }

        public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.SecretKey))
            {
                throw new EmailSendingException(
                    "Mailjet ApiKey and SecretKey are required. Set Mailjet:ApiKey and Mailjet:SecretKey in appsettings or environment.");
            }

            if (string.IsNullOrWhiteSpace(_settings.SenderEmail))
            {
                throw new EmailSendingException(
                    "Mailjet SenderEmail is not configured. Use a verified sender from Mailjet dashboard.");
            }

            var message = new MailjetMessage
            {
                From = new MailjetContact
                {
                    Email = _settings.SenderEmail.Trim(),
                    Name = string.IsNullOrWhiteSpace(_settings.SenderName) ? null : _settings.SenderName.Trim()
                },
                To = [new MailjetContact { Email = to.Trim() }],
                Subject = subject,
                TextPart = isHtml ? null : body,
                HtmlPart = isHtml ? body : null
            };

            var payload = new MailjetSendRequest { Messages = [message] };

            var client = _httpClientFactory.CreateClient(nameof(MailjetEmailService));
            using var request = new HttpRequestMessage(HttpMethod.Post, "send")
            {
                Content = JsonContent.Create(payload)
            };

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ApiKey}:{_settings.SecretKey}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            _logger.LogInformation("Sending email via Mailjet API to {To}", to);

            using var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent via Mailjet to {To}", to);
                return;
            }

            _logger.LogError(
                "Mailjet API failed. Status={StatusCode}, Body={Body}",
                (int)response.StatusCode,
                responseBody);

            throw new EmailSendingException(
                $"Mailjet API failed ({(int)response.StatusCode}). " +
                "Verify ApiKey, SecretKey, and sender address in Mailjet. " +
                $"Details: {responseBody}");
        }

        private sealed class MailjetSendRequest
        {
            [JsonPropertyName("Messages")]
            public List<MailjetMessage> Messages { get; set; } = new();
        }

        private sealed class MailjetMessage
        {
            [JsonPropertyName("From")]
            public MailjetContact From { get; set; } = new();

            [JsonPropertyName("To")]
            public List<MailjetContact> To { get; set; } = new();

            [JsonPropertyName("Subject")]
            public string Subject { get; set; } = string.Empty;

            [JsonPropertyName("TextPart")]
            public string? TextPart { get; set; }

            [JsonPropertyName("HTMLPart")]
            public string? HtmlPart { get; set; }
        }

        private sealed class MailjetContact
        {
            [JsonPropertyName("Email")]
            public string Email { get; set; } = string.Empty;

            [JsonPropertyName("Name")]
            public string? Name { get; set; }
        }
    }
}
