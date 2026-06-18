namespace BoardVerse.Core.Entities
{
    public class LobbyMember
    {
        public Guid Id { get; set; }
        public Guid LobbyId { get; set; }
        public Guid UserId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        public virtual Lobby Lobby { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}
