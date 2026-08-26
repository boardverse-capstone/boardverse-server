using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.User;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IUserManagementService
    {
        Task<PaginatedResponse<AdminUserDto>> GetAllAsync(AdminUserQueryDto query, CancellationToken cancellationToken = default);
        Task<AdminUserDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
        Task<AdminUserDto> CreateAsync(AdminCreateUserDto request, CancellationToken cancellationToken = default);
        Task<AdminUserDto> UpdateAsync(Guid id, AdminUpdateUserDto request, CancellationToken cancellationToken = default);
        Task DisableAsync(Guid id, CancellationToken cancellationToken = default);
        Task<AdminUserDto> BlockAsync(Guid id, AdminBlockUserDto request, CancellationToken cancellationToken = default);
        Task<AdminUserDto> UnblockAsync(Guid id, CancellationToken cancellationToken = default);
        Task<AdminUserDto> UpdateRoleAsync(Guid id, AdminUpdateUserRoleDto request, CancellationToken cancellationToken = default);
    }
}