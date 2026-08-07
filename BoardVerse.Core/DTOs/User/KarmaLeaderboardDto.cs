namespace BoardVerse.Core.DTOs.User
{
    /// <summary>
    /// K-06: Single entry in the global karma leaderboard.
    /// </summary>
    public class KarmaLeaderboardEntryDto
    {
        public int Rank { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public int KarmaPoints { get; set; }
        public string GamerTier { get; set; } = "Bronze";
    }

    /// <summary>
    /// K-06: Global karma leaderboard response.
    /// </summary>
    public class KarmaLeaderboardDto
    {
        public List<KarmaLeaderboardEntryDto> Entries { get; set; } = [];
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
