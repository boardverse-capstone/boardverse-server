namespace BoardVerse.Core.Settings
{
    public class BggSettings
    {
        public const string SectionName = "Bgg";

        /// <summary>
        /// Bearer token from https://boardgamegeek.com/applications (required since 2025).
        /// </summary>
        public string ApiToken { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = "https://boardgamegeek.com/xmlapi2";

        /// <summary>
        /// Delay between sequential API requests to respect BGG rate limits.
        /// </summary>
        public int RequestDelayMs { get; set; } = 1500;
    }
}
