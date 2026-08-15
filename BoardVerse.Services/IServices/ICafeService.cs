using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.DTOs.Cafe;

namespace BoardVerse.Services.IServices
{
    public interface ICafeService
    {
        /// <summary>
        /// Lấy thông tin chi tiết quán cafe (public endpoint cho player).
        /// Bao gồm: pricing, refund policy, seat availability, schedule overrides.
        /// Field nhạy cảm (OperationalStatusReason) bị ẩn cho player.
        /// Set <paramref name="includeSensitiveInfo"/>=true để lấy đầy đủ (chỉ manager/admin).
        /// </summary>
        /// <param name="cafeId">Mã cafe.</param>
        /// <param name="latitude">Vĩ độ player (optional, để tính distance).</param>
        /// <param name="longitude">Kinh độ player (optional, để tính distance).</param>
        /// <param name="includeSensitiveInfo">True nếu caller là manager/admin/staff và cần thấy lý do nội bộ.</param>
        Task<CafeDetailDto> GetCafeDetailAsync(
            Guid cafeId,
            double? latitude = null,
            double? longitude = null,
            bool includeSensitiveInfo = false);

        /// <summary>
        /// Lấy thông tin cơ bản quán cafe (cho legacy compatibility).
        /// </summary>
        Task<CafeDto> GetCafeAsync(Guid cafeId);
        Task<CafeDto> UpdateCafeAsync(Guid cafeId, Guid managerId, UpdateCafeRequestDto dto);

        /// <summary>
        /// Lấy danh sách quán mà manager sở hữu (kèm đầy đủ thông tin chi tiết cho manager).
        /// </summary>
        Task<IEnumerable<ManagerCafeDto>> GetManagerCafesAsync(Guid managerId);

        /// <summary>
        /// Lấy danh sách quán mà staff hiện đang làm việc (kèm thông tin chi tiết cho staff).
        /// </summary>
        Task<IEnumerable<ManagerCafeDto>> GetMyWorkplacesAsync(Guid currentStaffId);
        Task AddStaffAsync(Guid cafeId, Guid currentManagerId, AddStaffRequestDto dto);
        Task PromoteUserToStaffAsync(Guid cafeId, Guid currentManagerId, PromoteStaffRequestDto dto);
        Task<PaginatedResponse<StaffDto>> GetStaffListAsync(Guid cafeId, Guid currentManagerId, PaginationParams paginationParams);
        Task RemoveStaffAsync(Guid cafeId, Guid currentManagerId, Guid staffId);
        Task<NearbyCafeSearchResultDto> GetNearbyCafesAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid? gameTemplateId,
            string? name,
            PaginationParams paginationParams);

        Task<NearbyCafeSearchResultDto> GetNearbyCafesForCurrentUserAsync(
            Guid userId,
            double radiusKm,
            Guid? gameTemplateId,
            string? name,
            PaginationParams paginationParams);

        Task<PaginatedResponse<NearbyCafeDto>> SearchCafesAsync(
            string name,
            double? latitude,
            double? longitude,
            double? radiusKm,
            PaginationParams paginationParams);

        /// <summary>
        /// Lấy tất cả quán đang ACTIVE cho player (không filter Location, không yêu cầu gameTemplateId).
        /// </summary>
        Task<PaginatedResponse<NearbyCafeDto>> GetAllActiveCafesAsync(PaginationParams paginationParams);

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
