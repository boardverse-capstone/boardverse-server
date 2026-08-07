using BoardVerse.Core.Entities;

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
        Task<IReadOnlyList<(Guid userId, string username, string? avatarUrl, int karmaPoints, string gamerTier)>> GetKarmaLeaderboardAsync(int limit = 100);
    }
}
