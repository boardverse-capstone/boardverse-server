using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.Repositories
{
    public interface IUserManagementRepository
    {
        Task<bool> UserExistsAsync(string email, string username);
        Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null);
        Task<bool> EmailExistsAsync(string email, Guid? excludedUserId = null);
        Task<List<User>> GetAdminUsersAsync(AdminUserQueryDto query);
        Task<User?> GetByIdAsync(Guid userId);
        Task<User?> GetByIdWithProfileAsync(Guid userId);
        Task AddUserAsync(User user);
        Task SaveChangesAsync();
    }
}
