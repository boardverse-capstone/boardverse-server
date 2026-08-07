using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.User
{
    /// <summary>
    /// K-05: Player profile response with computed game stats.
    /// </summary>
    public class PlayerProfileWithStatsDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;

        // Identity
        public string? AvatarUrl { get; set; }
        public string? AvatarBorderUrl { get; set; }
        public string? CoverPhotoUrl { get; set; }
        public string? Bio { get; set; }

        // Personal
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        // Gamer stats
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = "Bronze";
        public int GlobalElo { get; set; }
        public int Level { get; set; }

        // K-05: Game stats (computed from MatchHistory)
        public int GamesPlayedCount { get; set; }
        public double WinRate { get; set; }
        public List<Guid>? FavoriteGameIds { get; set; }

        // K-05: Preferred play mode (Solo / Group)
        public PlayerPlayMode PreferredPlayMode { get; set; }

        public DateTime UpdatedAt { get; set; }
        public bool HasProfile { get; set; }
    }
}
