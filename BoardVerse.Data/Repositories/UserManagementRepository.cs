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

            if (!string.IsNullOrWhiteSpace(query.Role) && Enum.TryParse<UserRole>(query.Role, true, out var parsedRole))
            {
                usersQuery = usersQuery.Where(u => u.Role == parsedRole);
            }

            if (query.IsActive.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.IsActive == query.IsActive.Value);
            }

            if (query.IsBlocked.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.IsBlocked == query.IsBlocked.Value);
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
