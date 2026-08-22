using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Common;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface ICafePartnerApplicationService
    {
        Task<CafePartnerApplicationResponseDto> SubmitAsync(SubmitCafePartnerApplicationRequestDto request, Guid? submittedByUserId = null, CancellationToken cancellationToken = default);
        Task<CafePartnerApplicationResponseDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<CafePartnerApplicationResponseDto>> GetAllForAdminAsync(AdminCafePartnerApplicationQueryDto query, CancellationToken cancellationToken = default);

        Task<OnboardPartnerResultDto> ApproveAsync(Guid id, Guid adminId, CancellationToken cancellationToken = default);
        Task<CafePartnerApplicationResponseDto> RejectAsync(Guid id, Guid adminId, RejectCafePartnerApplicationRequestDto request, CancellationToken cancellationToken = default);

        Task<ManagerCafeProfileResponseDto> GetMyPartnerProfileAsync(Guid managerUserId, CancellationToken cancellationToken = default);
        Task<ManagerCafeProfileResponseDto> UpdateOperationalProfileAsync(Guid managerUserId, UpdateOperationalProfileRequestDto request, CancellationToken cancellationToken = default);
        Task<ManagerCafeProfileResponseDto> ActivateAsync(Guid managerUserId, CancellationToken cancellationToken = default);
        Task<ManagerCafeProfileResponseDto> ReopenAsync(Guid managerUserId, CancellationToken cancellationToken = default);
        Task<ManagerCafeProfileResponseDto> DeactivateAsync(Guid managerUserId, CancellationToken cancellationToken = default);
        Task<ManagerCafeProfileResponseDto> ClosePermanentlyAsync(Guid managerUserId, CancellationToken cancellationToken = default);
    }
}
