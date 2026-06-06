using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Username { get; set; }
        public required string Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? PasswordHash { get; set; }
        public UserRole Role { get; set; }
        public string Provider { get; set; } = "Local";
        public string? ProviderId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Profile navigation (one-to-one)
        public virtual UserProfile? Profile { get; set; }

        // Email verification
        public bool IsEmailVerified { get; set; } = false;
        public string? EmailVerificationToken { get; set; }
        public DateTime? EmailVerificationTokenExpiresAt { get; set; }

        // Password reset
        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetTokenExpiresAt { get; set; }

        // Account status
        public bool IsActive { get; set; } = true;
        public bool IsBlocked { get; set; } = false;
        public string? BlockReason { get; set; }
        public DateTime? BlockedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }
}
