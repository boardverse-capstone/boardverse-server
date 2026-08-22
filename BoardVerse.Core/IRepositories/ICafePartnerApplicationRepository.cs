using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ICafePartnerApplicationRepository
    {
        Task AddAsync(CafePartnerApplication application, CancellationToken cancellationToken = default);
        Task<CafePartnerApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<CafePartnerApplication?> GetApprovedByManagerUserIdAsync(Guid managerUserId, CancellationToken cancellationToken = default);
        Task<bool> HasOpenApplicationByEmailAsync(string email, CancellationToken cancellationToken = default);
        Task<bool> HasSevereDuplicateAsync(string businessLicense, string normalizedAddress, Guid? excludeApplicationId = null, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<CafePartnerApplication>> GetPagedAsync(AdminCafePartnerApplicationQueryDto query, CancellationToken cancellationToken = default);
        Task AddCafeAsync(Cafe cafe, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
