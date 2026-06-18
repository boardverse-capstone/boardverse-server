using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>Per-member match result submission for consensus (AC 4.2).</summary>
    public class MatchResult
    {
        public Guid Id { get; set; }
        public Guid LobbyId { get; set; }
        public Guid UserId { get; set; }
        public MatchOutcome Outcome { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual Lobby Lobby { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}
