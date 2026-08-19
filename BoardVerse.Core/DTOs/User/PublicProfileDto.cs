namespace BoardVerse.Core.DTOs.User
{
    public class ProfileDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? AvatarUrl { get; set; }
        public string? AvatarBorderUrl { get; set; }
        public string? Bio { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = "Bronze";
        public int GlobalElo { get; set; }
        public int Level { get; set; }
        public int CurrentExp { get; set; }
        public DateTime? LastActiveAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool HasProfile { get; set; }
        public bool IsFriendListPublic { get; set; }
        public string AcceptFriendRequestsFrom { get; set; } = "Everyone";
        public int FriendLimit { get; set; }
    }
}