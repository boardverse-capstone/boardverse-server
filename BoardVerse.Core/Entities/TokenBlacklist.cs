namespace BoardVerse.Core.Entities
{
    public class TokenBlacklist
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public required string Token { get; set; }
        private DateTime _expiresAt;
        public DateTime ExpiresAt
        {
            get => _expiresAt;
            set
            {
                if (value <= DateTime.UtcNow)
                    throw new ArgumentException("ExpiresAt must be in the future");
                _expiresAt = value;
            }
        }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? Reason { get; set; }

        // Navigation property
        public User? User { get; set; }
    }
}
