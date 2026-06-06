namespace BoardVerse.Core.Entities
{
    public class TokenBlacklist
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public required string Token { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? Reason { get; set; }

        // Navigation property
        public User? User { get; set; }
    }
}
