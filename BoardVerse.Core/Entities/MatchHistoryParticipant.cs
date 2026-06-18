using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class MatchHistoryParticipant
    {
        public Guid Id { get; set; }
        public Guid MatchHistoryId { get; set; }
        public Guid UserId { get; set; }
        public MatchOutcome ReportedOutcome { get; set; }
        public int EloBefore { get; set; }
        public int EloAfter { get; set; }
        public int EloDelta { get; set; }

        public virtual MatchHistory MatchHistory { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}
