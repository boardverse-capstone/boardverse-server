using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafeRepository
    {
        Task<Cafe?> GetByIdAsync(Guid id);
        Task<User?> GetUserByEmailAsync(string email);
        Task AddCafeStaffAsync(CafeStaff cafeStaff);
        Task AddUserAsync(User user);
        Task<bool> IsStaffMemberExistsAsync(Guid cafeId, Guid userId);
        Task<PaginatedResponse<StaffDto>> GetStaffPagedAsync(Guid cafeId, PaginationParams paginationParams);
        Task<CafeStaff?> GetCafeStaffAsync(Guid cafeId, Guid staffId);
        Task RemoveCafeStaffAsync(CafeStaff cafeStaff);
        Task<IEnumerable<Cafe>> GetCafesByStaffIdAsync(Guid staffId);
        Task SaveChangesAsync();
    }
}
