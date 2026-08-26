using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class UserManagementRepository : IUserManagementRepository
    {
        private readonly BoardVerseDbContext _context;

        public UserManagementRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task<bool> UserExistsAsync(string email, string username, CancellationToken cancellationToken = default)
        {
            return _context.Users.AnyAsync(u => u.Email == email || u.Username == username);
        }

        public Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null, CancellationToken cancellationToken = default)
        {
            return _context.Users.AnyAsync(u => u.Username == username && (!excludedUserId.HasValue || u.Id != excludedUserId.Value));
        }

        public Task<bool> EmailExistsAsync(string email, Guid? excludedUserId = null, CancellationToken cancellationToken = default)
        {
            return _context.Users.AnyAsync(u => u.Email == email && (!excludedUserId.HasValue || u.Id != excludedUserId.Value));
        }

        public async Task<PaginatedResponse<User>> GetAdminUsersAsync(AdminUserQueryDto query, CancellationToken cancellationToken = default)
        {
            var usersQuery = _context.Users.Include(u => u.Profile).AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                usersQuery = usersQuery.Where(u => u.Username.Contains(search) || u.Email.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(query.Role) && UserRoleParser.TryParse(query.Role, out var parsedRole))
            {
                usersQuery = usersQuery.Where(u => u.Role == parsedRole);
            }

            if (query.IsActive.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.IsActive == query.IsActive.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.AccountStatus)
                && Enum.TryParse<UserAccountStatus>(query.AccountStatus, ignoreCase: true, out var accountStatus))
            {
                usersQuery = usersQuery.Where(u => u.AccountStatus == accountStatus);
            }

            var totalItems = await usersQuery.CountAsync();
            var items = await usersQuery
                .OrderByDescending(u => u.CreatedAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalItems / (double)query.PageSize);

            return new PaginatedResponse<User>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = query.Page,
                    PageSize = query.PageSize,
                    TotalItems = totalItems,
                    TotalPages = totalPages
                }
            };
        }

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        }

        public Task<User?> GetByIdWithProfileAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<IReadOnlyList<User>> SearchByUsernameAsync(
            string keyword,
            Guid excludeUserId,
            int limit = 20,
            IReadOnlyCollection<Guid>? blockedUserIds = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return Array.Empty<User>();

            var pattern = keyword.Trim().ToLower();
            var blocked = blockedUserIds ?? Array.Empty<Guid>();

            var query = _context.Users
                .Include(u => u.Profile)
                .Where(u => u.Id != excludeUserId
                    && u.IsActive
                    && u.AccountStatus == UserAccountStatus.Active
                    && u.Username.ToLower().Contains(pattern));

            if (blocked.Count > 0)
            {
                query = query.Where(u => !blocked.Contains(u.Id));
            }

            return await query
                .OrderBy(u => u.Username)
                .Take(Math.Clamp(limit, 1, 50))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
        {
            if (userIds == null || userIds.Count == 0) return Array.Empty<User>();
            var ids = userIds.ToHashSet();
            return await _context.Users
                .Include(u => u.Profile)
                .Where(u => ids.Contains(u.Id) && u.IsActive && u.AccountStatus == UserAccountStatus.Active)
                .ToListAsync();
        }

        public async Task UpdateLastActiveAsync(Guid userId, DateTime lastActiveAt, CancellationToken cancellationToken = default)
        {
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null) return;
            profile.LastActiveAt = lastActiveAt;
            profile.UpdatedAt = lastActiveAt;
            await _context.SaveChangesAsync();
        }

        public Task AddUserAsync(User user, CancellationToken cancellationToken = default)
        {
            _context.Users.Add(user);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _context.SaveChangesAsync();
        }
    }
}
