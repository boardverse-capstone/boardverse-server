namespace BoardVerse.Core.DTOs.Admin
{
    public class UserKarmaAlertDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = string.Empty;
        public DateTime ProfileUpdatedAt { get; set; }
    }
}
