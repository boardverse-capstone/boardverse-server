using BoardVerse.Core.Messages;

namespace BoardVerse.Core.Entities
{
    public class RefreshToken
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
                    throw new ArgumentException(ApiErrorMessages.Entity.ExpiresAtMustBeFuture);
                _expiresAt = value;
            }
        }
        public bool IsRevoked { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RevokedAt { get; set; }

        // Navigation property
        public User? User { get; set; }
    }
}
