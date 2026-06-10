namespace BoardVerse.Core.Settings
{
    public class EmailSettings
    {
        public const string SectionName = "Email";

        /// <summary>
        /// Smtp = send via SMTP. Console = local dev (log only, no send).
        /// </summary>
        public string Provider { get; set; } = "Console";
    }
}
