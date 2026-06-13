namespace BoardVerse.Core.Settings
{
    public class BrevoSettings
    {
        public const string SectionName = "Brevo";

        public string ApiKey { get; set; } = string.Empty;
        public string ApiBaseUrl { get; set; } = "https://api.brevo.com";
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = "BoardVerse";
    }
}
