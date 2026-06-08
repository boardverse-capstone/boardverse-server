using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Xml;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.BGG;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services
{
    public class BggApiService : IBggApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<BggApiService> _logger;
        private readonly BggSettings _settings;

        public BggApiService(
            HttpClient httpClient,
            ILogger<BggApiService> logger,
            IOptions<BggSettings> settings)
        {
            _httpClient = httpClient;
            _logger = logger;
            _settings = settings.Value;

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; BoardVerse/1.0; +https://boardverse.app)");
            _httpClient.Timeout = TimeSpan.FromSeconds(60);

            if (!string.IsNullOrWhiteSpace(_settings.ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.ApiToken.Trim());
            }
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiToken);

        public async Task<BggGameDto?> GetGameByIdAsync(int bggGameId)
        {
            var result = await GetGamesByIdsAsync([bggGameId]);
            return result.FirstOrDefault();
        }

        public async Task<List<BggGameDto>> GetGamesByIdsAsync(List<int> bggGameIds)
        {
            if (bggGameIds == null || bggGameIds.Count == 0)
                return [];

            if (!IsConfigured)
            {
                _logger.LogWarning(
                    "BGG API token is not configured. Set Bgg:ApiToken in appsettings or BGG_API_TOKEN env var. " +
                    "See https://boardgamegeek.com/applications");
                return [];
            }

            var games = new List<BggGameDto>();
            var baseUrl = _settings.BaseUrl.TrimEnd('/');

            for (var i = 0; i < bggGameIds.Count; i++)
            {
                var bggGameId = bggGameIds[i];
                var url = $"{baseUrl}/thing?id={bggGameId}&stats=1";

                try
                {
                    var response = await CallWithRetryAsync(url);
                    var parsed = ParseBggXml(response);
                    games.AddRange(parsed);

                    if (i < bggGameIds.Count - 1 && _settings.RequestDelayMs > 0)
                        await Task.Delay(_settings.RequestDelayMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching BGG game {BggGameId}", bggGameId);
                }
            }

            return games;
        }

        private async Task<string> CallWithRetryAsync(string url, int maxRetries = 5)
        {
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException(
                        "BGG API returned 401 Unauthorized. Verify Bgg:ApiToken is valid and approved at " +
                        "https://boardgamegeek.com/applications");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    var delay = 2000 * (int)Math.Pow(2, attempt - 1);
                    _logger.LogInformation(
                        "BGG queued request, retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})",
                        delay, attempt, maxRetries);
                    await Task.Delay(delay);
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var delay = 5000 * (int)Math.Pow(2, attempt - 1);
                    _logger.LogInformation(
                        "BGG rate limited, waiting {Delay}ms (attempt {Attempt}/{MaxRetries})",
                        delay, attempt, maxRetries);
                    await Task.Delay(delay);
                    continue;
                }

                response.EnsureSuccessStatusCode();
            }

            throw new InvalidOperationException("BGG API max retries exceeded");
        }

        private List<BggGameDto> ParseBggXml(string xml)
        {
            var games = new List<BggGameDto>();

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);

                var items = doc.SelectNodes("//items/item");
                if (items == null) return games;

                foreach (XmlNode item in items)
                {
                    var bggGameIdStr = item.Attributes?["id"]?.Value;
                    var bggGameId = int.TryParse(bggGameIdStr, out var parsedId) ? parsedId : 0;
                    if (bggGameId == 0) continue;

                    var rawDescription = GetNodeValue(item, ".//description");
                    var catalog = BggKnownGameCatalog.GetById(bggGameId);

                    var game = new BggGameDto
                    {
                        Id = Guid.NewGuid(),
                        BggGameId = bggGameId,
                        Name = GetNodeValue(item, ".//name[@type='primary']")
                               ?? GetNodeValue(item, ".//name")
                               ?? catalog?.Name
                               ?? "Unknown",
                        Description = CleanDescription(rawDescription) ?? catalog?.Description,
                        ThumbnailUrl = GetNodeValue(item, ".//thumbnail"),
                        ImageUrl = GetNodeValue(item, ".//image"),
                        YearPublished = int.TryParse(GetNodeValue(item, ".//yearpublished"), out var year) ? year : 0,
                        MinPlayers = int.TryParse(GetNodeValue(item, ".//minplayers"), out var min) ? min : catalog?.MinPlayers ?? 1,
                        MaxPlayers = int.TryParse(GetNodeValue(item, ".//maxplayers"), out var max) ? max : catalog?.MaxPlayers ?? 4,
                        MinPlayTime = int.TryParse(GetNodeValue(item, ".//minplaytime"), out var minTime) ? minTime : 30,
                        MaxPlayTime = int.TryParse(GetNodeValue(item, ".//maxplaytime"), out var maxTime) ? maxTime : 60,
                        PlayingTime = int.TryParse(GetNodeValue(item, ".//playingtime"), out var playTime)
                            ? playTime
                            : catalog?.PlayTime ?? 60
                    };

                    var categoryNodes = item.SelectNodes(".//link[@type='boardgamecategory']");
                    if (categoryNodes != null)
                    {
                        foreach (XmlNode cat in categoryNodes)
                        {
                            var value = cat.Attributes?["value"]?.Value;
                            if (!string.IsNullOrEmpty(value))
                                game.Categories.Add(value);
                        }
                    }

                    var mechanicNodes = item.SelectNodes(".//link[@type='boardgamemechanic']");
                    if (mechanicNodes != null)
                    {
                        foreach (XmlNode mech in mechanicNodes)
                        {
                            var value = mech.Attributes?["value"]?.Value;
                            if (!string.IsNullOrEmpty(value))
                                game.Mechanics.Add(value);
                        }
                    }

                    game.Components = BuildComponents(bggGameId, rawDescription);
                    games.Add(game);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing BGG XML response");
            }

            return games;
        }

        private static List<BggComponentDto> BuildComponents(int bggGameId, string? description)
        {
            var catalogComponents = BggKnownGameCatalog.GetComponents(bggGameId);
            if (catalogComponents.Count > 0)
            {
                return catalogComponents
                    .Select(c => new BggComponentDto { Name = c.Name, Quantity = c.Quantity })
                    .ToList();
            }

            return ExtractComponentsFromDescription(description);
        }

        private static string? GetNodeValue(XmlNode parent, string xpath)
        {
            var node = parent.SelectSingleNode(xpath);
            return node?.Attributes?["value"]?.Value ?? node?.InnerText;
        }

        private static string? CleanDescription(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var decoded = WebUtility.HtmlDecode(html);
            var withoutTags = Regex.Replace(decoded, "<[^>]+>", " ");
            var normalized = Regex.Replace(withoutTags, @"\s+", " ").Trim();
            return normalized.Length > 2000 ? normalized[..2000] : normalized;
        }

        private static List<BggComponentDto> ExtractComponentsFromDescription(string? description)
        {
            var components = new List<BggComponentDto>();
            if (string.IsNullOrEmpty(description))
                return components;

            var patterns = new Dictionary<string, string>
            {
                { @"(\d+)\s+cards?", "Cards" },
                { @"(\d+)\s+tokens?", "Tokens" },
                { @"(\d+)\s+meeples?", "Meeples" },
                { @"(\d+)\s+dice", "Dice" },
                { @"(\d+)\s+d6", "D6 Dice" },
                { @"(\d+)\s+boards?", "Game Board" },
                { @"(\d+)\s+cubes?", "Cubes" },
                { @"(\d+)\s+discs?", "Discs" },
                { @"(\d+)\s+markers?", "Markers" },
                { @"(\d+)\s+counters?", "Counters" }
            };

            foreach (var (pattern, name) in patterns)
            {
                var matches = Regex.Matches(description, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out var quantity))
                    {
                        components.Add(new BggComponentDto { Name = name, Quantity = quantity });
                    }
                }
            }

            if (components.Count == 0)
            {
                components.Add(new BggComponentDto { Name = "Game Board", Quantity = 1 });
                components.Add(new BggComponentDto { Name = "Cards", Quantity = 50 });
                components.Add(new BggComponentDto { Name = "Tokens", Quantity = 20 });
            }

            return components;
        }
    }
}
