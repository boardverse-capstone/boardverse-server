namespace BoardVerse.Core.DTOs.Pos
{
    public class ActiveSessionDto
    {
        public Guid Id { get; set; }
        public Guid HostId { get; set; }
        public string HostName { get; set; } = string.Empty;
        public Guid? LobbyId { get; set; }
        public Guid CafeTableId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public int DefaultPlayTimeMinutes { get; set; }
        public DateTime StartedAt { get; set; }
        public int ElapsedMinutes { get; set; }
        public int EstimatedRemainingMinutes { get; set; }
        public IReadOnlyList<ActiveSessionMemberDto> Members { get; set; } = [];
        public IReadOnlyList<ActiveSessionGameDto> Games { get; set; } = [];
    }
}
