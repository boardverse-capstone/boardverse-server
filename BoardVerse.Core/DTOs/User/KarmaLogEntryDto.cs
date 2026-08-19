using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.User
{
    public class KarmaLogEntryDto
    {
        public Guid Id { get; set; }
        public int KarmaChange { get; set; }
        public int KarmaBefore { get; set; }
        public int KarmaAfter { get; set; }
        public KarmaViolationCategory ViolationCategory { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
