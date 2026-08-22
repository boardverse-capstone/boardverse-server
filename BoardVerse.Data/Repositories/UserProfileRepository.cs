using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class UserProfileRepository : IUserProfileRepository
    {
        private readonly BoardVerseDbContext _context;

        public UserProfileRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task<User?> GetByIdWithProfileAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == userId);
        }

        public Task<UserProfile?> GetProfileByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Set<UserProfile>().FirstOrDefaultAsync(p => p.UserId == userId);
        }

        public async Task<IReadOnlyDictionary<Guid, UserProfile>> GetProfilesByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
        {
            if (userIds.Count == 0)
            {
                return new Dictionary<Guid, UserProfile>();
            }
            var list = await _context.Set<UserProfile>()
                .Where(p => userIds.Contains(p.UserId))
                .ToListAsync();
            return list.ToDictionary(p => p.UserId);
        }

        public Task AddUserProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
        {
            _context.UserProfiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task AddPlayerLocationHistoryAsync(PlayerLocationHistory history, CancellationToken cancellationToken = default)
        {
            _context.PlayerLocationHistories.Add(history);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<KarmaLog>> GetKarmaLogsAsync(Guid userId, int limit = 50, CancellationToken cancellationToken = default)
        {
            return await _context.KarmaLogs
                .Where(k => k.UserId == userId)
                .OrderByDescending(k => k.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _context.SaveChangesAsync();
        }

        // === Admin: Reports ===

        public async Task<int> CountUsersAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Users.CountAsync();
        }

        // === K-05: Player profile game stats ===

        public async Task<(int gamesPlayed, int gamesWon)> GetMatchHistoryStatsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var participantCount = await _context.MatchHistoryParticipants
                .Where(p => p.UserId == userId)
                .CountAsync(cancellationToken);

            var wonCount = await _context.MatchHistories
                .Where(m => m.WinnerUserId == userId)
                .CountAsync(cancellationToken);

            return (participantCount, wonCount);
        }

        // === K-06: Karma + Elo leaderboard ===

        public async Task<IReadOnlyList<KarmaLeaderboardRow>> GetKarmaLeaderboardAsync(int offset, int limit, CancellationToken cancellationToken = default)
        {
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = 1;

            var query =
                from u in _context.Users
                join p in _context.UserProfiles on u.Id equals p.UserId
                where u.IsActive && p.IsActive
                orderby p.KarmaPoints descending, u.Username ascending
                select new KarmaLeaderboardRow
                {
                    UserId = u.Id,
                    Username = u.Username,
                    DisplayName = p.FirstName == null && p.LastName == null
                        ? null
                        : $"{p.FirstName} {p.LastName}".Trim(),
                    AvatarUrl = p.AvatarUrl,
                    KarmaPoints = p.KarmaPoints,
                    GlobalElo = p.GlobalElo,
                    Level = p.Level,
                    GamerTier = p.GamerTier
                };

            return await query.Skip(offset).Take(limit).ToListAsync();
        }

        public async Task<long> CountActiveKarmaUsersAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .Where(u => u.IsActive)
                .Join(_context.UserProfiles.Where(p => p.IsActive),
                    u => u.Id, p => p.UserId, (u, p) => new { u, p })
                .LongCountAsync();
        }

        public async Task<IReadOnlyList<EloLeaderboardRow>> GetEloLeaderboardAsync(int offset, int limit, CancellationToken cancellationToken = default)
        {
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = 1;

            var query =
                from u in _context.Users
                join p in _context.UserProfiles on u.Id equals p.UserId
                where u.IsActive && p.IsActive
                orderby p.GlobalElo descending, u.Username ascending
                select new EloLeaderboardRow
                {
                    UserId = u.Id,
                    Username = u.Username,
                    DisplayName = p.FirstName == null && p.LastName == null
                        ? null
                        : $"{p.FirstName} {p.LastName}".Trim(),
                    AvatarUrl = p.AvatarUrl,
                    KarmaPoints = p.KarmaPoints,
                    GlobalElo = p.GlobalElo,
                    Level = p.Level,
                    GamerTier = p.GamerTier
                };

            return await query.Skip(offset).Take(limit).ToListAsync();
        }

        public async Task<long> CountActiveEloUsersAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .Where(u => u.IsActive)
                .Join(_context.UserProfiles.Where(p => p.IsActive),
                    u => u.Id, p => p.UserId, (u, p) => new { u, p })
                .LongCountAsync();
        }

        public async Task<IReadOnlyList<LeaderboardRankRow>> GetLevelLeaderboardAsync(int offset, int limit, CancellationToken cancellationToken = default)
        {
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = 1;

            // Primary sort: Level DESC, Exp DESC (người chơi cao level + nhiều exp hơn xếp trước).
            // Tie-break: Username ASC.
            var query =
                from u in _context.Users
                join p in _context.UserProfiles on u.Id equals p.UserId
                where u.IsActive && p.IsActive
                orderby p.Level descending, p.CurrentExp descending, u.Username ascending
                select new LeaderboardRankRow
                {
                    UserId = u.Id,
                    Username = u.Username,
                    DisplayName = p.FirstName == null && p.LastName == null
                        ? null
                        : $"{p.FirstName} {p.LastName}".Trim(),
                    AvatarUrl = p.AvatarUrl,
                    KarmaPoints = p.KarmaPoints,
                    GlobalElo = p.GlobalElo,
                    Level = p.Level,
                    GamerTier = p.GamerTier
                };

            return await query.Skip(offset).Take(limit).ToListAsync();
        }

        public async Task<long> CountActiveLevelUsersAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .Where(u => u.IsActive)
                .Join(_context.UserProfiles.Where(p => p.IsActive),
                    u => u.Id, p => p.UserId, (u, p) => new { u, p })
                .LongCountAsync();
        }

        public async Task<LeaderboardRankRow?> GetUserRankAsync(Guid userId, LeaderboardMetric metric, CancellationToken cancellationToken = default)
        {
            // Base projection reused for both metrics.
            var profileQuery =
                from u in _context.Users
                join p in _context.UserProfiles on u.Id equals p.UserId
                where u.Id == userId && u.IsActive && p.IsActive
                select new LeaderboardRankRow
                {
                    UserId = u.Id,
                    Username = u.Username,
                    DisplayName = p.FirstName == null && p.LastName == null
                        ? null
                        : $"{p.FirstName} {p.LastName}".Trim(),
                    AvatarUrl = p.AvatarUrl,
                    KarmaPoints = p.KarmaPoints,
                    GlobalElo = p.GlobalElo,
                    Level = p.Level,
                    GamerTier = p.GamerTier
                };

            var profile = await profileQuery.FirstOrDefaultAsync();
            if (profile == null) return null;

            // Tie-breaker rule (matches the leaderboard ORDER BY):
            //   DESC by metric, then ASC by Username for stability.
            // "Rank" = number of users strictly ahead of this user + 1.
            // For simplicity and DB-portability, the caller (LeaderboardService)
            // reuses the same Skip(n).Take(limit) ordering to determine the
            // 1-based rank after fetching the row above — see service code.
            return profile;
        }
    }
}
