using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IUserProfileRepository
    {
        Task<User?> GetByIdWithProfileAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<UserProfile?> GetProfileByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<Guid, UserProfile>> GetProfilesByUserIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
        Task AddUserProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);
        Task AddPlayerLocationHistoryAsync(PlayerLocationHistory history, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<KarmaLog>> GetKarmaLogsAsync(Guid userId, int limit = 50, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        // === Admin: Reports ===
        Task<int> CountUsersAsync(CancellationToken cancellationToken = default);

        // === K-05: Player profile game stats ===
        Task<(int gamesPlayed, int gamesWon)> GetMatchHistoryStatsAsync(Guid userId, CancellationToken cancellationToken = default);

        // === K-06: Karma leaderboard ===
        Task<IReadOnlyList<KarmaLeaderboardRow>> GetKarmaLeaderboardAsync(int offset, int limit, CancellationToken cancellationToken = default);

        Task<long> CountActiveKarmaUsersAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<EloLeaderboardRow>> GetEloLeaderboardAsync(int offset, int limit, CancellationToken cancellationToken = default);

        Task<long> CountActiveEloUsersAsync(CancellationToken cancellationToken = default);

        // === K-06: Level leaderboard (BR-K-06 level) ===
        Task<IReadOnlyList<LeaderboardRankRow>> GetLevelLeaderboardAsync(int offset, int limit, CancellationToken cancellationToken = default);

        Task<long> CountActiveLevelUsersAsync(CancellationToken cancellationToken = default);

        /// <summary>Compute the rank of a single user for karma, elo, or level.</summary>
        Task<LeaderboardRankRow?> GetUserRankAsync(Guid userId, LeaderboardMetric metric, CancellationToken cancellationToken = default);
    }

    public enum LeaderboardMetric
    {
        Karma = 0,
        Elo = 1,
        Level = 2
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
