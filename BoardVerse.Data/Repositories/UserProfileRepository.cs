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

        public Task SaveChangesAsync()
        {
            return _context.SaveChangesAsync();
        }
    }
}
