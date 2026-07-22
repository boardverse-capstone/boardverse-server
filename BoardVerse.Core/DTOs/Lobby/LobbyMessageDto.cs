namespace BoardVerse.Core.DTOs.Lobby
{
    public class LobbyMessageDto
    {
        public Guid Id { get; set; }
        public Guid LobbyId { get; set; }
        
        /// <summary>Nullable for system messages.</summary>
        public Guid? SenderId { get; set; }
        
        public string SenderName { get; set; } = string.Empty;
        public string? SenderAvatarUrl { get; set; }
        public string Content { get; set; } = string.Empty;
        public bool IsSystem { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}