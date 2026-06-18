namespace BoardVerse.Core.Settings
{
    public class BggSettings
    {
        public const string SectionName = "Bgg";

        public string ApiBaseUrl { get; set; } = "https://boardgamegeek.com/xmlapi2";
        public string ApiToken { get; set; } = string.Empty;
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int MaxRetryAttempts { get; set; } = 5;
        public int RetryDelayMilliseconds { get; set; } = 2000;
    }
}
