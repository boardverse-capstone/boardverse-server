using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class Lobby
    {
        public Guid Id { get; set; }
        public Guid GameTemplateId { get; set; }
        public Guid? ActiveSessionId { get; set; }
        public LobbyStatus Status { get; set; } = LobbyStatus.Open;
        public DateTime? RatingOpenedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual GameTemplate GameTemplate { get; set; } = null!;
        public virtual ActiveSession? ActiveSession { get; set; }
        public virtual ICollection<LobbyMember> Members { get; set; } = [];
    }
}
