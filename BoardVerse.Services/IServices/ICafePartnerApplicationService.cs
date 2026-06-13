using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.CafePartner;

namespace BoardVerse.Services.IServices
{
    public interface ICafePartnerApplicationService
    {
        Task<CafePartnerApplicationResponseDto> SubmitAsync(SubmitCafePartnerApplicationRequestDto request, Guid? submittedByUserId = null);
        Task<CafePartnerApplicationResponseDto> GetByIdAsync(Guid id);
        Task<PaginatedResponse<CafePartnerApplicationResponseDto>> GetAllForAdminAsync(AdminCafePartnerApplicationQueryDto query);

        Task<OnboardPartnerResultDto> ApproveAsync(Guid id, Guid adminId);
        Task<CafePartnerApplicationResponseDto> RejectAsync(Guid id, Guid adminId, RejectCafePartnerApplicationRequestDto request);

        Task<CafePartnerApplicationResponseDto> GetMyPartnerProfileAsync(Guid managerUserId);
        Task<CafePartnerApplicationResponseDto> UpdateOperationalProfileAsync(Guid managerUserId, UpdateOperationalProfileRequestDto request);
        Task<CafePartnerApplicationResponseDto> ActivateAsync(Guid managerUserId);
        Task<CafePartnerApplicationResponseDto> DeactivateAsync(Guid managerUserId);
    }
}
