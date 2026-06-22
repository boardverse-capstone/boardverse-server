using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class KarmaLog
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public KarmaViolationCategory ViolationCategory { get; set; }
        public KarmaLogSource Source { get; set; }
        public decimal KarmaPointsChange { get; set; }
        public int KarmaBefore { get; set; }
        public int KarmaAfter { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Guid? RelatedLobbyId { get; set; }
        public Guid? PerformedByUserId { get; set; }
        public bool IsAdminAdjustment { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual User User { get; set; } = null!;
        public virtual User? PerformedByUser { get; set; }
    }
}
