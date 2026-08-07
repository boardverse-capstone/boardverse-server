using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.DTOs.Cafe;

namespace BoardVerse.Services.IServices
{
    public interface ICafeService
    {
        Task<CafeDto> GetCafeAsync(Guid cafeId);
        Task<CafeDto> UpdateCafeAsync(Guid cafeId, Guid managerId, UpdateCafeRequestDto dto);
        Task<IEnumerable<CafeDto>> GetManagerCafesAsync(Guid managerId);
        Task AddStaffAsync(Guid cafeId, Guid currentManagerId, AddStaffRequestDto dto);
        Task PromoteUserToStaffAsync(Guid cafeId, Guid currentManagerId, PromoteStaffRequestDto dto);
        Task<PaginatedResponse<StaffDto>> GetStaffListAsync(Guid cafeId, Guid currentManagerId, PaginationParams paginationParams);
        Task RemoveStaffAsync(Guid cafeId, Guid currentManagerId, Guid staffId);
        Task<IEnumerable<CafeDto>> GetMyWorkplacesAsync(Guid currentStaffId);
        Task<NearbyCafeSearchResultDto> GetNearbyCafesAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid gameTemplateId,
            PaginationParams paginationParams);

        Task<NearbyCafeSearchResultDto> GetNearbyCafesForCurrentUserAsync(
            Guid userId,
            double radiusKm,
            Guid gameTemplateId,
            PaginationParams paginationParams);

        Task<AdminCafeOperationalStatusResultDto> SetOperationalStatusByAdminAsync(
            Guid cafeId,
            AdminSetCafeOperationalStatusRequestDto request);

        Task UpdateSePayConfigAsync(Guid cafeId, Guid managerId, UpdateSePayConfigRequestDto dto);

        /// <summary>Mobile task #12: cập nhật chính sách hoàn cọc (BR-18).</summary>
        Task<RefundPolicyResponseDto> UpdateRefundPolicyAsync(Guid cafeId, Guid managerId, UpdateRefundPolicyRequestDto dto);

        /// <summary>Mobile task #13: cập nhật biểu phí (BR-01/BR-04).</summary>
        Task<CafePricingConfigResponseDto> UpdatePricingConfigAsync(Guid cafeId, Guid managerId, UpdatePricingConfigRequestDto dto);

        // === Admin: Cafe management ===
        Task<AdminCafeListResponseDto> GetAdminCafesAsync(
            int page, int pageSize, string? searchTerm, string? status, Guid? managerId);
        Task<AdminCafeDetailDto?> GetAdminCafeDetailAsync(Guid cafeId);
        Task<AdminCafeDetailDto> AdminCreateCafeAsync(AdminCreateCafeRequestDto request);
        Task<AdminCafeDetailDto> AdminUpdateCafeAsync(Guid cafeId, AdminUpdateCafeRequestDto request);
        Task AdminDeleteCafeAsync(Guid cafeId);
    }
}
