using BoardVerse.Core.DTOs.CafePartner;
using BoardVerse.Core.Common;

namespace BoardVerse.Services.IServices
{
    public interface ICafePartnerApplicationService
    {
        Task<CafePartnerApplicationResponseDto> SubmitAsync(SubmitCafePartnerApplicationRequestDto request, Guid? submittedByUserId = null);
        Task<CafePartnerApplicationResponseDto> GetByIdAsync(Guid id);
        Task<PaginatedResponse<CafePartnerApplicationResponseDto>> GetAllForAdminAsync(AdminCafePartnerApplicationQueryDto query);

        Task<OnboardPartnerResultDto> ApproveAsync(Guid id, Guid adminId);
        Task<CafePartnerApplicationResponseDto> RejectAsync(Guid id, Guid adminId, RejectCafePartnerApplicationRequestDto request);

        Task<ManagerCafeProfileResponseDto> GetMyPartnerProfileAsync(Guid managerUserId);
        Task<ManagerCafeProfileResponseDto> UpdateOperationalProfileAsync(Guid managerUserId, UpdateOperationalProfileRequestDto request);
        Task<ManagerCafeProfileResponseDto> ActivateAsync(Guid managerUserId);
        Task<ManagerCafeProfileResponseDto> ReopenAsync(Guid managerUserId);
        Task<ManagerCafeProfileResponseDto> DeactivateAsync(Guid managerUserId);
        Task<ManagerCafeProfileResponseDto> ClosePermanentlyAsync(Guid managerUserId);
    }
}
