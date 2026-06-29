using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafeRepository
    {
        Task<Cafe?> GetByIdAsync(Guid id);
        Task<Cafe?> GetActiveByIdAsync(Guid id);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByIdAsync(Guid userId);
        Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null);
        Task AddCafeStaffAsync(CafeStaff cafeStaff);
        Task AddUserAsync(User user);
        Task<bool> IsStaffMemberExistsAsync(Guid cafeId, Guid userId);
        Task<int> CountActiveStaffAssignmentsAsync(Guid userId);
        Task<PaginatedResponse<StaffDto>> GetStaffPagedAsync(Guid cafeId, PaginationParams paginationParams);
        Task<CafeStaff?> GetCafeStaffAsync(Guid cafeId, Guid staffId);
        Task RemoveCafeStaffAsync(CafeStaff cafeStaff);
        Task<IEnumerable<Cafe>> GetCafesByStaffIdAsync(Guid staffId);
        Task<IEnumerable<Cafe>> GetCafesByManagerIdAsync(Guid managerId);
        Task<PaginatedResponse<NearbyCafeDto>> GetNearbyAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid? gameTemplateId,
            PaginationParams paginationParams);
        Task EnrichNearbyWithGameWaitAsync(IList<NearbyCafeDto> cafes, Guid gameTemplateId);
        Task<IReadOnlyList<NearbyAlternativeGameSuggestionDto>> GetAlternativeGameSuggestionsAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid gameTemplateId,
            int limit = 10);
        Task<Cafe?> GetPartnerCafeByManagerIdAsync(Guid managerUserId);
        Task SyncCafeTablesAsync(Guid cafeId, IReadOnlyList<string> tableNames);
        Task<bool> HasActiveBookingsAsync(Guid cafeId);
        Task SaveChangesAsync();
    }
}
