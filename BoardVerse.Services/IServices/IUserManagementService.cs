using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.User;

namespace BoardVerse.Services.IServices
{
    public interface IUserManagementService
    {
        Task<PaginatedResponse<AdminUserDto>> GetAllAsync(AdminUserQueryDto query);
        Task<AdminUserDto> GetAsync(Guid id);
        Task<AdminUserDto> CreateAsync(AdminCreateUserDto request);
        Task<AdminUserDto> UpdateAsync(Guid id, AdminUpdateUserDto request);
        Task DisableAsync(Guid id);
        Task<AdminUserDto> BlockAsync(Guid id, AdminBlockUserDto request);
        Task<AdminUserDto> UnblockAsync(Guid id);
        Task<AdminUserDto> UpdateRoleAsync(Guid id, AdminUpdateUserRoleDto request);
    }
}