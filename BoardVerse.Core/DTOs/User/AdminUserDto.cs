namespace BoardVerse.Core.DTOs.User
{
    public class AdminUserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "Player";
        public bool IsBlocked { get; set; }
        public string? BlockReason { get; set; }
        public DateTime? BlockedAt { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string? AvatarUrl { get; set; }
        public string? Bio { get; set; }
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = "Bronze";
        public int GlobalElo { get; set; }
        public int Level { get; set; }
    }
}