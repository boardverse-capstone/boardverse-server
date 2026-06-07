using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class CafeRepository : ICafeRepository
    {
        private readonly BoardVerseDbContext _context;

        public CafeRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public async Task<Cafe?> GetByIdAsync(Guid id)
        {
            return await _context.Cafes
                .Include(c => c.StaffMembers)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public Task AddCafeStaffAsync(CafeStaff cafeStaff)
        {
            _context.CafeStaffs.Add(cafeStaff);
            return Task.CompletedTask;
        }

        public Task AddUserAsync(User user)
        {
            _context.Users.Add(user);
            return Task.CompletedTask;
        }

        public async Task<bool> IsStaffMemberExistsAsync(Guid cafeId, Guid userId)
        {
            return await _context.CafeStaffs
                .AnyAsync(cs => cs.CafeId == cafeId && cs.UserId == userId && cs.IsActive);
        }

        public async Task<PaginatedResponse<StaffDto>> GetStaffPagedAsync(Guid cafeId, PaginationParams paginationParams)
        {
            var query = _context.CafeStaffs
                .Include(cs => cs.User)
                .Where(cs => cs.CafeId == cafeId && cs.IsActive)
                .Select(cs => new StaffDto
                {
                    UserId = cs.UserId,
                    Email = cs.User.Email,
                    FullName = cs.User.Username, // Note: Using Username as FullName until proper FullName field is added
                    JoinedAt = cs.JoinedAt
                });

            var totalItems = await query.CountAsync();
            var items = await query
                .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
                .Take(paginationParams.PageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalItems / (double)paginationParams.PageSize);

            return new PaginatedResponse<StaffDto>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = paginationParams.PageNumber,
                    PageSize = paginationParams.PageSize,
                    TotalItems = totalItems,
                    TotalPages = totalPages
                }
            };
        }

        public async Task<CafeStaff?> GetCafeStaffAsync(Guid cafeId, Guid staffId)
        {
            return await _context.CafeStaffs
                .Include(cs => cs.Cafe)
                .FirstOrDefaultAsync(cs => cs.CafeId == cafeId && cs.UserId == staffId && cs.IsActive);
        }

        public async Task RemoveCafeStaffAsync(CafeStaff cafeStaff)
        {
            cafeStaff.IsActive = false;
            _context.CafeStaffs.Update(cafeStaff);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<Cafe>> GetCafesByStaffIdAsync(Guid staffId)
        {
            return await _context.CafeStaffs
                .Include(cs => cs.Cafe)
                .Where(cs => cs.UserId == staffId && cs.IsActive)
                .Select(cs => cs.Cafe)
                .ToListAsync();
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
