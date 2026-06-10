namespace BoardVerse.Core.Settings
{
    public class SmtpSettings
    {
        public const string SectionName = "Smtp";

        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 25;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string From { get; set; } = "noreply@boardverse.local";
        public bool EnableSsl { get; set; }
    }
}
