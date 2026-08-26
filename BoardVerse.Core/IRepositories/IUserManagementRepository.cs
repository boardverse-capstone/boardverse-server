using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.User;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IUserManagementRepository
    {
        Task<bool> UserExistsAsync(string email, string username, CancellationToken cancellationToken = default);
        Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null, CancellationToken cancellationToken = default);
        Task<bool> EmailExistsAsync(string email, Guid? excludedUserId = null, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<User>> GetAdminUsersAsync(AdminUserQueryDto query, CancellationToken cancellationToken = default);
        Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<User?> GetByIdWithProfileAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tìm user theo username (case-insensitive contains) cho friend search.
        /// Trả về tối đa limit users, loại trừ excludeUserId và các blockedUserIds.
        /// </summary>
        /// <param name="excludeUserId">User hiện tại (bỏ chính mình).</param>
        /// <param name="blockedUserIds">Danh sách user đã chặn (cả 2 chiều) — null/empty thì bỏ qua.</param>
        Task<IReadOnlyList<User>> SearchByUsernameAsync(
            string keyword,
            Guid excludeUserId,
            int limit = 20,
            IReadOnlyCollection<Guid>? blockedUserIds = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách user theo danh sách UserId (cho friend suggestions, mutual friends, activity).
        /// </summary>
        Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cập nhật LastActiveAt của user (gọi từ middleware hoặc background job).
        /// </summary>
        Task UpdateLastActiveAsync(Guid userId, DateTime lastActiveAt, CancellationToken cancellationToken = default);

        Task AddUserAsync(User user, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
