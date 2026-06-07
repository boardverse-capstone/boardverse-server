using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;

namespace BoardVerse.Services.IServices
{
    public interface ICafeService
    {
        Task AddStaffAsync(Guid cafeId, Guid currentManagerId, AddStaffRequestDto dto);
        Task<PaginatedResponse<StaffDto>> GetStaffListAsync(Guid cafeId, Guid currentManagerId, PaginationParams paginationParams);
        Task RemoveStaffAsync(Guid cafeId, Guid currentManagerId, Guid staffId);
        Task<IEnumerable<CafeDto>> GetMyWorkplacesAsync(Guid currentStaffId);
    }
}
