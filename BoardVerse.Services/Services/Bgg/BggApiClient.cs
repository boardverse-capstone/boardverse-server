using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Bgg
{
    public sealed class BggApiClient
    {
        private const string HttpClientName = nameof(BggApiClient);
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BggSettings _settings;
        private readonly ILogger<BggApiClient> _logger;

        public BggApiClient(
            IHttpClientFactory httpClientFactory,
            IOptions<BggSettings> settings,
            ILogger<BggApiClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        public Task<string> GetThingXmlAsync(int bggId, CancellationToken cancellationToken = default) =>
            GetXmlAsync($"thing?id={bggId}&stats=1", cancellationToken, expectStatistics: true);

        public Task<string> SearchXmlAsync(string query, CancellationToken cancellationToken = default)
        {
            var encoded = WebUtility.UrlEncode(query.Trim());
            return GetXmlAsync($"search?query={encoded}&type=boardgame", cancellationToken, expectStatistics: false);
        }

        private async Task<string> GetXmlAsync(
            string relativePath,
            CancellationToken cancellationToken,
            bool expectStatistics)
        {
            // Lớp retry 1: HTTP status (202 Accepted + retryable transport errors) — delay ngắn.
            var xml = await GetXmlWithHttpRetryAsync(relativePath, cancellationToken);

            // Lớp retry 2 (chỉ cho `thing?id=...&stats=1`): XML thiếu <statistics>.
            // BGG thỉnh thoảng trả 200 OK nhưng chưa kịp gắn <statistics> vào XML
            // (do hàng đợi async phía BGG). Retry với delay dài hơn vì BGG cần
            // thời gian tính toán averageweight.
            if (expectStatistics && !ContainsStatistics(xml))
            {
                _logger.LogInformation(
                    "BGG API returned XML without <statistics> for {Path}; retrying after {Delay}ms to wait for BGG to compute community vote average.",
                    relativePath,
                    _settings.StatsRetryDelayMilliseconds);

                await Task.Delay(_settings.StatsRetryDelayMilliseconds, cancellationToken);
                xml = await GetXmlWithHttpRetryAsync(relativePath, cancellationToken);

                if (!ContainsStatistics(xml))
                {
                    _logger.LogWarning(
                        "BGG API still missing <statistics> for {Path} after stats retry; downstream will treat Weight as null and admin may need to set manually.",
                        relativePath);
                }
            }

            return xml;
        }

        /// <summary>
        /// Thực hiện HTTP retry loop — bao gồm HTTP 202 (queued) và exponential backoff
        /// thông qua <see cref="BggSettings.RetryDelayMilliseconds"/>. Không retry XML-level issue.
        /// </summary>
        private async Task<string> GetXmlWithHttpRetryAsync(string relativePath, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var url = BuildUrl(relativePath);

            for (var attempt = 1; attempt <= _settings.MaxRetryAttempts; attempt++)
            {
                using var response = await client.GetAsync(url, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Accepted)
                {
                    _logger.LogInformation(
                        "BGG API queued request ({Path}); retry {Attempt}/{Max}.",
                        relativePath,
                        attempt,
                        _settings.MaxRetryAttempts);
                    await Task.Delay(_settings.RetryDelayMilliseconds, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            throw new HttpRequestException(
                $"BGG API did not return data for '{relativePath}' after {_settings.MaxRetryAttempts} attempts.");
        }

        /// <summary>
        /// Kiểm tra XML BGG có chứa khối <c>&lt;statistics&gt;</c> hay không.
        /// Dùng cheap regex check trên raw string (không parse XML đầy đủ).
        /// </summary>
        private static bool ContainsStatistics(string xml) =>
            xml.Contains("<statistics", StringComparison.Ordinal);

        private string BuildUrl(string relativePath)
        {
            var baseUrl = _settings.ApiBaseUrl.TrimEnd('/');
            return $"{baseUrl}/{relativePath}";
        }

        public static string HttpClientNameValue => HttpClientName;
    }

    internal static class BggXmlParser
    {
        public static BggThingData ParseThing(string xml)
        {
            var doc = XDocument.Parse(xml);
            var item = doc.Root?.Elements("item").FirstOrDefault()
                ?? throw new InvalidOperationException(ApiErrorMessages.System.BggInvalidResponse);

            var id = ParseInt(item.Attribute("id")?.Value) ?? 0;
            var name = item.Elements("name")
                .FirstOrDefault(e => e.Attribute("type")?.Value == "primary")
                ?.Attribute("value")?.Value
                ?? item.Elements("name").FirstOrDefault()?.Attribute("value")?.Value
                ?? string.Empty;

            var categories = item.Elements("link")
                .Where(e => e.Attribute("type")?.Value == "boardgamecategory")
                .Select(e => e.Attribute("value")?.Value ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mechanics = item.Elements("link")
                .Where(e => e.Attribute("type")?.Value == "boardgamemechanic")
                .Select(e => e.Attribute("value")?.Value ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var minPlayers = ParseInt(item.Element("minplayers")?.Attribute("value")?.Value) ?? 1;
            var maxPlayers = ParseInt(item.Element("maxplayers")?.Attribute("value")?.Value) ?? minPlayers;
            if (maxPlayers < minPlayers)
                (minPlayers, maxPlayers) = (maxPlayers, minPlayers);

            var playTime = ParseInt(item.Element("playingtime")?.Attribute("value")?.Value)
                ?? ParseInt(item.Element("maxplaytime")?.Attribute("value")?.Value)
                ?? ParseInt(item.Element("minplaytime")?.Attribute("value")?.Value)
                ?? 60;

            // BGG <statistics><averageweight value="X.X"/> — chỉ trả về khi request có stats=1
            // và game đã có đủ community vote (thường >0).
            var averageWeightText = item
                .Element("statistics")
                ?.Element("averageweight")
                ?.Attribute("value")
                ?.Value;
            double? weight = ParseDouble(averageWeightText);
            if (weight is < 1.0 or > 5.0)
                weight = null;

            return new BggThingData
            {
                Id = id,
                Name = DecodeHtml(name),
                Description = StripHtml(item.Element("description")?.Value),
                ThumbnailUrl = item.Element("thumbnail")?.Value?.Trim(),
                MinPlayers = Math.Max(1, minPlayers),
                MaxPlayers = Math.Max(1, maxPlayers),
                PlayTime = Math.Max(1, playTime),
                Weight = weight,
                Categories = categories,
                Mechanics = mechanics
            };
        }

        public static IReadOnlyList<BggSearchItem> ParseSearch(string xml)
        {
            var doc = XDocument.Parse(xml);
            return doc.Root?.Elements("item")
                .Select(item => new BggSearchItem
                {
                    Id = ParseInt(item.Attribute("id")?.Value) ?? 0,
                    Name = DecodeHtml(item.Element("name")?.Attribute("value")?.Value ?? string.Empty),
                    YearPublished = ParseInt(item.Element("yearpublished")?.Attribute("value")?.Value)
                })
                .Where(i => i.Id > 0 && !string.IsNullOrWhiteSpace(i.Name))
                .ToList()
                ?? [];
        }

        private static int? ParseInt(string? value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

        private static double? ParseDouble(string? value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

        private static string DecodeHtml(string value) =>
            WebUtility.HtmlDecode(value ?? string.Empty).Trim();

        private static string? StripHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var decoded = WebUtility.HtmlDecode(html);
            var sb = new StringBuilder(decoded.Length);
            var insideTag = false;

            foreach (var ch in decoded)
            {
                if (ch == '<')
                {
                    insideTag = true;
                    continue;
                }

                if (ch == '>')
                {
                    insideTag = false;
                    continue;
                }

                if (!insideTag)
                    sb.Append(ch);
            }

            var text = sb.ToString().Replace('\n', ' ').Replace('\r', ' ');
            while (text.Contains("  ", StringComparison.Ordinal))
                text = text.Replace("  ", " ", StringComparison.Ordinal);

            text = text.Trim();
            if (text.Length > 2000)
                text = text[..2000];

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
