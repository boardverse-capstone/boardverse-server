namespace BoardVerse.Core.DTOs.Lobby
{
    public class LobbyMemberDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public int KarmaPoints { get; set; } = 100;
        public DateTime JoinedAt { get; set; }
        public DateTime? ReadyAt { get; set; }
        public bool IsActive { get; set; }
        public bool IsHost { get; set; }

        /// <summary>Trạng thái member: Joined/Ready/Kicked/Left.</summary>
        public string Status { get; set; } = "Joined";
    }
}