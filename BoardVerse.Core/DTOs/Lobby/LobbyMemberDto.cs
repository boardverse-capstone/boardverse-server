namespace BoardVerse.Core.DTOs.Lobby
{
    public class LobbyMemberDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public DateTime JoinedAt { get; set; }
        public bool IsActive { get; set; }
        public bool IsHost { get; set; }
    }
}
