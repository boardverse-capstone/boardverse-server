using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.User
{
    /// <summary>
    /// Single row in a karma/elo leaderboard response.
    /// </summary>
    public class LeaderboardEntryDto
    {
        public int Rank { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;

        /// <summary>Optional display name (FirstName + LastName). Null nếu user chưa set.</summary>
        public string? DisplayName { get; set; }

        public string? AvatarUrl { get; set; }
        public int KarmaPoints { get; set; }
        public int GlobalElo { get; set; }
        public int Level { get; set; }
        public GamerTier GamerTier { get; set; } = GamerTier.Bronze;
    }

    /// <summary>
    /// K-06: Compact karma-only entry (kept for backward compatibility).
    /// </summary>
    public class KarmaLeaderboardEntryDto
    {
        public int Rank { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public int KarmaPoints { get; set; }
        public GamerTier GamerTier { get; set; } = GamerTier.Bronze;
    }

    /// <summary>
    /// K-06: Compact elo-only entry (kept for backward compatibility).
    /// </summary>
    public class EloLeaderboardEntryDto
    {
        public int Rank { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public int GlobalElo { get; set; }
        public GamerTier GamerTier { get; set; } = GamerTier.Bronze;
        public int Level { get; set; }
    }

    /// <summary>
    /// K-06: Global karma leaderboard response.
    /// </summary>
    public class KarmaLeaderboardDto
    {
        public List<KarmaLeaderboardEntryDto> Entries { get; set; } = [];
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// K-06: Global elo leaderboard response.
    /// </summary>
    public class EloLeaderboardDto
    {
        public List<EloLeaderboardEntryDto> Entries { get; set; } = [];
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Shared shape for paginated leaderboard responses. Includes the optional
    /// <see cref="UserRank"/> block so authenticated viewers can locate themselves
    /// when they are not in the current page (BR §K-06 UX).
    /// </summary>
    public class LeaderboardPagedDto<TEntry>
    {
        public List<TEntry> Entries { get; set; } = [];
        public int Offset { get; set; }
        public int Limit { get; set; }
        public long TotalCount { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Rank của viewer đang đăng nhập (null nếu anonymous hoặc chưa có rank).</summary>
        public LeaderboardEntryDto? UserRank { get; set; }
    }
}
