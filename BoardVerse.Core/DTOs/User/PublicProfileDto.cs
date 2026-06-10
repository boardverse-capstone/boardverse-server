namespace BoardVerse.Core.DTOs.User
{
    public class ProfileDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Bio { get; set; }
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = "Bronze";
        public int GlobalElo { get; set; }
        public int Level { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}