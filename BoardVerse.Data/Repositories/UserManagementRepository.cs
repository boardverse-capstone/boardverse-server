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

        public Task<bool> UserExistsAsync(string email, string username)
        {
            return _context.Users.AnyAsync(u => u.Email == email || u.Username == username);
        }

        public Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null)
        {
            return _context.Users.AnyAsync(u => u.Username == username && (!excludedUserId.HasValue || u.Id != excludedUserId.Value));
        }

        public Task<bool> EmailExistsAsync(string email, Guid? excludedUserId = null)
        {
            return _context.Users.AnyAsync(u => u.Email == email && (!excludedUserId.HasValue || u.Id != excludedUserId.Value));
        }

        public async Task<PaginatedResponse<User>> GetAdminUsersAsync(AdminUserQueryDto query)
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

        public Task<User?> GetByIdAsync(Guid userId)
        {
            return _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        }

        public Task<User?> GetByIdWithProfileAsync(Guid userId)
        {
            return _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<IReadOnlyList<User>> SearchByUsernameAsync(string keyword, Guid excludeUserId, int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return Array.Empty<User>();

            var pattern = keyword.Trim().ToLower();
            return await _context.Users
                .Include(u => u.Profile)
                .Where(u => u.Id != excludeUserId
                    && u.IsActive
                    && u.AccountStatus == UserAccountStatus.Active
                    && u.Username.ToLower().Contains(pattern))
                .OrderBy(u => u.Username)
                .Take(Math.Clamp(limit, 1, 50))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> userIds)
        {
            if (userIds == null || userIds.Count == 0) return Array.Empty<User>();
            var ids = userIds.ToHashSet();
            return await _context.Users
                .Include(u => u.Profile)
                .Where(u => ids.Contains(u.Id) && u.IsActive && u.AccountStatus == UserAccountStatus.Active)
                .ToListAsync();
        }

        public async Task UpdateLastActiveAsync(Guid userId, DateTime lastActiveAt)
        {
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null) return;
            profile.LastActiveAt = lastActiveAt;
            profile.UpdatedAt = lastActiveAt;
            await _context.SaveChangesAsync();
        }

        public Task AddUserAsync(User user)
        {
            _context.Users.Add(user);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            return _context.SaveChangesAsync();
        }
    }
}
