using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafePartnerApplicationRepository
    {
        Task AddAsync(CafePartnerApplication application);
        Task<CafePartnerApplication?> GetByIdAsync(Guid id);
        Task<CafePartnerApplication?> GetApprovedByManagerUserIdAsync(Guid managerUserId);
        Task<bool> HasOpenApplicationByEmailAsync(string email);
        Task<bool> HasSevereDuplicateAsync(string businessLicense, string normalizedAddress, Guid? excludeApplicationId = null);
        Task<PaginatedResponse<CafePartnerApplication>> GetPagedAsync(AdminCafePartnerApplicationQueryDto query);
        Task AddCafeAsync(Cafe cafe);
        Task SaveChangesAsync();
    }
}
