using BoardVerse.Core.Entities;
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

        public Task<User?> GetByIdWithProfileAsync(Guid userId)
        {
            return _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == userId);
        }

        public Task<UserProfile?> GetProfileByUserIdAsync(Guid userId)
        {
            return _context.Set<UserProfile>().FirstOrDefaultAsync(p => p.UserId == userId);
        }

        public async Task<IReadOnlyDictionary<Guid, UserProfile>> GetProfilesByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds)
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

        public Task AddUserProfileAsync(UserProfile profile)
        {
            _context.UserProfiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task AddPlayerLocationHistoryAsync(PlayerLocationHistory history)
        {
            _context.PlayerLocationHistories.Add(history);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<KarmaLog>> GetKarmaLogsAsync(Guid userId, int limit = 50)
        {
            return await _context.KarmaLogs
                .Where(k => k.UserId == userId)
                .OrderByDescending(k => k.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        public Task SaveChangesAsync()
        {
            return _context.SaveChangesAsync();
        }

        // === Admin: Reports ===

        public async Task<int> CountUsersAsync()
        {
            return await _context.Users.CountAsync();
        }

        // === K-05: Player profile game stats ===

        public async Task<(int gamesPlayed, int gamesWon)> GetMatchHistoryStatsAsync(Guid userId)
        {
            var participantCount = await _context.MatchHistoryParticipants
                .Where(p => p.UserId == userId)
                .CountAsync();

            var wonCount = await _context.MatchHistories
                .Where(m => m.WinnerUserId == userId)
                .CountAsync();

            return (participantCount, wonCount);
        }

        // === K-06: Karma leaderboard ===

        public async Task<IReadOnlyList<(Guid userId, string username, string? avatarUrl, int karmaPoints, string gamerTier)>> GetKarmaLeaderboardAsync(int limit = 100)
        {
            var results = await _context.Users
                .Where(u => u.IsActive)
                .Join(_context.UserProfiles.Where(p => p.IsActive),
                    u => u.Id,
                    p => p.UserId,
                    (u, p) => new { u.Id, u.Username, p.AvatarUrl, p.KarmaPoints, p.GamerTier })
                .OrderByDescending(x => x.KarmaPoints)
                .Take(limit)
                .ToListAsync();

            return results
                .Select(x => (x.Id, x.Username, x.AvatarUrl, x.KarmaPoints, x.GamerTier.ToString()))
                .ToList();
        }
    }
}
