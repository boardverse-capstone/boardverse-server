using BoardVerse.Core.Common;
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
    }
}
