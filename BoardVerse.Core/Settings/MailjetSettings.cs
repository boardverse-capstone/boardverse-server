namespace BoardVerse.Core.Settings
{
    public class MailjetSettings
    {
        public const string SectionName = "Mailjet";

        public string ApiKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string ApiBaseUrl { get; set; } = "https://api.mailjet.com/v3.1";
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = "BoardVerse";
    }
}
