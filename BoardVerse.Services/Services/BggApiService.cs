using System.Xml;
using BoardVerse.Core.DTOs.BGG;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class BggApiService : IBggApiService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://boardgamegeek.com/xmlapi/thing";

        public BggApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<BggGameDto?> GetGameByIdAsync(int bggGameId)
        {
            var result = await GetGamesByIdsAsync(new List<int> { bggGameId });
            return result.FirstOrDefault();
        }

        public async Task<List<BggGameDto>> GetGamesByIdsAsync(List<int> bggGameIds)
        {
            if (bggGameIds == null || !bggGameIds.Any())
                return new List<BggGameDto>();

            var ids = string.Join(",", bggGameIds);
            var url = $"{BaseUrl}?id={ids}&stats=1";

            try
            {
                var response = await CallWithRetryAsync(url);
                var games = ParseBggXml(response);
                return games;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching games from BGG: {ex.Message}");
                return new List<BggGameDto>();
            }
        }

        private async Task<string> CallWithRetryAsync(string url, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync();
                    }
                    
                    // BGG returns 202 when request is queued
                    if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        var delay = 2000 * attempt; // Exponential backoff: 2s, 4s, 6s
                        Console.WriteLine($"Request queued by BGG, retrying in {delay}ms... (Attempt {attempt}/{maxRetries})");
                        await Task.Delay(delay);
                        continue;
                    }
                    
                    // Rate limit (429)
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        var delay = 5000 * attempt; // 5s, 10s, 15s
                        Console.WriteLine($"Rate limited by BGG, waiting {delay}ms... (Attempt {attempt}/{maxRetries})");
                        await Task.Delay(delay);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == maxRetries)
                        throw;
                    
                    Console.WriteLine($"HTTP error on attempt {attempt}/{maxRetries}: {ex.Message}");
                    await Task.Delay(1000 * attempt);
                }
            }

            throw new Exception("Max retries exceeded");
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
                    var game = new BggGameDto
                    {
                        Id = Guid.NewGuid(),
                        Name = GetNodeValue(item, ".//name[@type='primary']") ?? 
                              GetNodeValue(item, ".//name") ?? "Unknown",
                        Description = GetNodeValue(item, ".//description"),
                        ThumbnailUrl = GetNodeValue(item, ".//thumbnail"),
                        ImageUrl = GetNodeValue(item, ".//image"),
                        YearPublished = int.TryParse(GetNodeValue(item, ".//yearpublished"), out var year) ? year : 0,
                        MinPlayers = int.TryParse(GetNodeValue(item, ".//minplayers"), out var min) ? min : 1,
                        MaxPlayers = int.TryParse(GetNodeValue(item, ".//maxplayers"), out var max) ? max : 4,
                        MinPlayTime = int.TryParse(GetNodeValue(item, ".//minplaytime"), out var minTime) ? minTime : 30,
                        MaxPlayTime = int.TryParse(GetNodeValue(item, ".//maxplaytime"), out var maxTime) ? maxTime : 60,
                        PlayingTime = int.TryParse(GetNodeValue(item, ".//playingtime"), out var playTime) ? playTime : 60
                    };

                    // Parse categories
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

                    // Parse mechanics
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

                    // Parse components from description or other fields
                    // Note: BGG doesn't have structured component data, so we'll extract from description
                    game.Components = ExtractComponentsFromDescription(game.Description, game.Name);

                    games.Add(game);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing XML: {ex.Message}");
            }

            return games;
        }

        private string? GetNodeValue(XmlNode parent, string xpath)
        {
            var node = parent.SelectSingleNode(xpath);
            return node?.Attributes?["value"]?.Value ?? node?.InnerText;
        }

        private List<BggComponentDto> ExtractComponentsFromDescription(string? description, string gameName)
        {
            var components = new List<BggComponentDto>();
            
            if (string.IsNullOrEmpty(description))
                return components;

            // Common component patterns in BGG descriptions
            var patterns = new Dictionary<string, string>
            {
                { @"(\d+)\s+cards?", "Cards" },
                { @"(\d+)\s+tokens?", "Tokens" },
                { @"(\d+)\s+meeples?", "Meeples" },
                { @"(\d+)\s+dice", "Dice" },
                { @"(\d+)\s+d6", "D6 Dice" },
                { @"(\d+)\s+board", "Game Board" },
                { @"(\d+)\s+cube", "Cubes" },
                { @"(\d+)\s+disc", "Discs" },
                { @"(\d+)\s+marker", "Markers" },
                { @"(\d+)\s+counter", "Counters" }
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    description.ToLower(), 
                    pattern.Key, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out var quantity))
                    {
                        components.Add(new BggComponentDto
                        {
                            Name = pattern.Value,
                            Quantity = quantity
                        });
                    }
                }
            }

            // If no components found, add default based on game type
            if (!components.Any())
            {
                components.Add(new BggComponentDto { Name = "Game Board", Quantity = 1 });
                components.Add(new BggComponentDto { Name = "Cards", Quantity = 50 });
                components.Add(new BggComponentDto { Name = "Tokens", Quantity = 20 });
            }

            return components;
        }
    }
}
