using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Email
{
    public class BrevoEmailService : IEmailService
    {
        private readonly BrevoSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BrevoEmailService> _logger;

        public BrevoEmailService(
            IOptions<BrevoSettings> settings,
            IHttpClientFactory httpClientFactory,
            ILogger<BrevoEmailService> logger)
        {
            _settings = settings.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            _logger.LogInformation(
                "Brevo configured — ApiBaseUrl: {ApiBaseUrl}, Sender: {SenderName} <{SenderEmail}>",
                _settings.ApiBaseUrl,
                _settings.SenderName,
                _settings.SenderEmail);
        }

        public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                throw new EmailSendingException(
                    "Brevo ApiKey is required. Set Brevo:ApiKey or Brevo__ApiKey in appsettings or environment.");
            }

            if (string.IsNullOrWhiteSpace(_settings.SenderEmail))
            {
                throw new EmailSendingException(
                    "Brevo SenderEmail is not configured. Verify sender in Brevo dashboard (Senders).");
            }

            var payload = new BrevoSendRequest
            {
                Sender = new BrevoContact
                {
                    Email = _settings.SenderEmail.Trim(),
                    Name = string.IsNullOrWhiteSpace(_settings.SenderName) ? null : _settings.SenderName.Trim()
                },
                To = [new BrevoContact { Email = to.Trim() }],
                Subject = subject,
                TextContent = isHtml ? null : body,
                HtmlContent = isHtml ? body : null
            };

            var sendUri = BuildSendUri(_settings.ApiBaseUrl);
            var client = _httpClientFactory.CreateClient(nameof(BrevoEmailService));
            using var request = new HttpRequestMessage(HttpMethod.Post, sendUri)
            {
                Content = JsonContent.Create(payload),
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            request.Headers.Add("api-key", _settings.ApiKey);
            request.Headers.UserAgent.ParseAdd("BoardVerse/1.0");

            _logger.LogInformation("Sending email via Brevo API to {To} at {SendUri}", to, sendUri);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Brevo HTTP connection failed to {SendUri}", sendUri);
                throw new EmailSendingException(
                    "Cannot connect to Brevo API. Verify Brevo__ApiKey and Brevo__ApiBaseUrl=https://api.brevo.com on hosting.",
                    ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Brevo API request timed out to {SendUri}", sendUri);
                throw new EmailSendingException("Brevo API request timed out.", ex);
            }

            using (response)
            {
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Email sent via Brevo to {To}", to);
                    return;
                }

                _logger.LogError(
                    "Brevo API failed. Status={StatusCode}, Body={Body}",
                    (int)response.StatusCode,
                    responseBody);

                throw new EmailSendingException(
                    $"Brevo API failed ({(int)response.StatusCode}). " +
                    "Verify ApiKey, account validation, and sender email in Brevo. " +
                    $"Details: {responseBody}");
            }
        }

        private static Uri BuildSendUri(string? apiBaseUrl)
        {
            var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
                ? "https://api.brevo.com"
                : apiBaseUrl.Trim().TrimEnd('/');

            if (!Uri.TryCreate($"{baseUrl}/v3/smtp/email", UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new EmailSendingException(
                    $"Invalid Brevo ApiBaseUrl '{apiBaseUrl}'. Use https://api.brevo.com");
            }

            return uri;
        }

        private sealed class BrevoSendRequest
        {
            [JsonPropertyName("sender")]
            public BrevoContact Sender { get; set; } = new();

            [JsonPropertyName("to")]
            public List<BrevoContact> To { get; set; } = [];

            [JsonPropertyName("subject")]
            public string Subject { get; set; } = string.Empty;

            [JsonPropertyName("textContent")]
            public string? TextContent { get; set; }

            [JsonPropertyName("htmlContent")]
            public string? HtmlContent { get; set; }
        }

        private sealed class BrevoContact
        {
            [JsonPropertyName("email")]
            public string Email { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }
    }
}
