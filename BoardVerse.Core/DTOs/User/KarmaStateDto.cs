namespace BoardVerse.Core.DTOs.User
{
    public class KarmaStateDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = "Bronze";
        public string? AvatarUrl { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}