using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IUserManagementRepository
    {
        Task<bool> UserExistsAsync(string email, string username);
        Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null);
        Task<bool> EmailExistsAsync(string email, Guid? excludedUserId = null);
        Task<PaginatedResponse<User>> GetAdminUsersAsync(AdminUserQueryDto query);
        Task<User?> GetByIdAsync(Guid userId);
        Task<User?> GetByIdWithProfileAsync(Guid userId);

        /// <summary>
        /// Tìm user theo username (case-insensitive contains) cho friend search.
        /// Trả về tối đa limit users, loại trừ excludeUserId.
        /// </summary>
        Task<IReadOnlyList<User>> SearchByUsernameAsync(string keyword, Guid excludeUserId, int limit = 20);

        /// <summary>
        /// Lấy danh sách user theo danh sách UserId (cho friend suggestions, mutual friends, activity).
        /// </summary>
        Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> userIds);

        /// <summary>
        /// Cập nhật LastActiveAt của user (gọi từ middleware hoặc background job).
        /// </summary>
        Task UpdateLastActiveAsync(Guid userId, DateTime lastActiveAt);

        Task AddUserAsync(User user);
        Task SaveChangesAsync();
    }
}
