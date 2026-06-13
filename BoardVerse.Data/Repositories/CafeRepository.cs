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

        public async Task<Cafe?> GetActiveByIdAsync(Guid id)
        {
            return await _context.Cafes
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == id &&
                    c.IsActive &&
                    (c.PartnerOperationalStatus == null ||
                     c.PartnerOperationalStatus == Core.Enum.CafePartnerOperationalStatus.Active));
        }

        public async Task<User?> GetUserByIdAsync(Guid userId)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null)
        {
            var query = _context.Users.Where(u => u.Username == username);
            if (excludedUserId.HasValue)
            {
                query = query.Where(u => u.Id != excludedUserId.Value);
            }

            return await query.AnyAsync();
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

        public async Task<int> CountActiveStaffAssignmentsAsync(Guid userId)
        {
            return await _context.CafeStaffs
                .CountAsync(cs => cs.UserId == userId && cs.IsActive);
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
                    Username = cs.User.Username,
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

        public Task RemoveCafeStaffAsync(CafeStaff cafeStaff)
        {
            cafeStaff.IsActive = false;
            _context.CafeStaffs.Update(cafeStaff);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<Cafe>> GetCafesByManagerIdAsync(Guid managerId)
        {
            return await _context.Cafes
                .AsNoTracking()
                .Where(c => c.ManagerId == managerId && c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();
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
