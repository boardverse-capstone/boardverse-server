namespace BoardVerse.Core.DTOs.User
{
    public class ProfileDetailDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string? Bio { get; set; }
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = "Bronze";
        public int GlobalElo { get; set; }
        public int Level { get; set; }

        // PII
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? HomeAddress { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}