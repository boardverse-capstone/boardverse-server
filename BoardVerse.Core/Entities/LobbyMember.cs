namespace BoardVerse.Core.Entities
{
    public class LobbyMember
    {
        public Guid Id { get; set; }
        public Guid LobbyId { get; set; }
        public Guid UserId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        /// <summary>Host-led check-in: true nếu đây là người khởi tạo phòng chờ.</summary>
        public bool IsHost { get; set; }

        public virtual Lobby Lobby { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}
