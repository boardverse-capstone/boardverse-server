using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories
{
    public interface IUserProfileRepository
    {
        Task<User?> GetByIdWithProfileAsync(Guid userId);
        Task<UserProfile?> GetProfileByUserIdAsync(Guid userId);
        Task<IReadOnlyDictionary<Guid, UserProfile>> GetProfilesByUserIdsAsync(IReadOnlyCollection<Guid> userIds);
        Task AddUserProfileAsync(UserProfile profile);
        Task AddPlayerLocationHistoryAsync(PlayerLocationHistory history);
        Task<IReadOnlyList<KarmaLog>> GetKarmaLogsAsync(Guid userId, int limit = 50);
        Task SaveChangesAsync();

        // === Admin: Reports ===
        Task<int> CountUsersAsync();

        // === K-05: Player profile game stats ===
        Task<(int gamesPlayed, int gamesWon)> GetMatchHistoryStatsAsync(Guid userId);

        // === K-06: Karma leaderboard ===
        Task<IReadOnlyList<KarmaLeaderboardRow>> GetKarmaLeaderboardAsync(int offset, int limit);

        Task<long> CountActiveKarmaUsersAsync();

        Task<IReadOnlyList<EloLeaderboardRow>> GetEloLeaderboardAsync(int offset, int limit);

        Task<long> CountActiveEloUsersAsync();

        /// <summary>Compute the rank of a single user for either karma or elo.</summary>
        Task<LeaderboardRankRow?> GetUserRankAsync(Guid userId, LeaderboardMetric metric);
    }

    public enum LeaderboardMetric
    {
        Karma = 0,
        Elo = 1
    }

    /// <summary>Shared shape returned by both karma/elo leaderboard queries.</summary>
    public class LeaderboardRankRow
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public int KarmaPoints { get; set; }
        public int GlobalElo { get; set; }
        public int Level { get; set; }
        public GamerTier GamerTier { get; set; } = GamerTier.Bronze;
    }

    public class KarmaLeaderboardRow : LeaderboardRankRow { }

    public class EloLeaderboardRow : LeaderboardRankRow { }
}
