using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class MatchHistory
    {
        public Guid Id { get; set; }
        public Guid LobbyId { get; set; }
        public Guid GameTemplateId { get; set; }
        public MatchConsensusStatus Status { get; set; } = MatchConsensusStatus.Finalized;
        public Guid? WinnerUserId { get; set; }
        public bool IsDraw { get; set; }
        public DateTime FinalizedAt { get; set; } = DateTime.UtcNow;

        public virtual Lobby Lobby { get; set; } = null!;
        public virtual GameTemplate GameTemplate { get; set; } = null!;
        public virtual User? WinnerUser { get; set; }
        public virtual ICollection<MatchHistoryParticipant> Participants { get; set; } = [];
    }
}
