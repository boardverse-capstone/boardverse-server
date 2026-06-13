using BoardVerse.Core.Enum;
using System;

namespace BoardVerse.Core.Entities
{
    public class UserProfile
    {
        public Guid UserId { get; set; } // Shared PK and FK to User

        // --- Gamer Identity ---
        public string? AvatarUrl { get; set; }

        public string? AvatarBorderUrl { get; set; }

        public string? Bio { get; set; }

        public int KarmaPoints { get; set; } = 100;
        public GamerTier GamerTier { get; set; } = GamerTier.Bronze;

        public int GlobalElo { get; set; } = 1200;

        public int Level { get; set; } = 1;

        public int CurrentExp { get; set; } = 0;

        // --- Real Life Personal Information ---
        public string? FirstName { get; set; }

        public string? LastName { get; set; }
        public DateOnly? DateOfBirth { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        public virtual User User { get; set; } = null!;
    }
}