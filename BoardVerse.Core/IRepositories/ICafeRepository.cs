using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafeRepository
    {
        Task<Cafe?> GetByIdAsync(Guid id);
        Task<Cafe?> GetActiveByIdAsync(Guid id);
        Task<Cafe?> GetByIdWithInventoriesAsync(Guid id);

        /// <summary>
        /// Lấy Cafe kèm Manager navigation (FK User) — dùng cho Admin GET /api/admin/cafes/{id}
        /// cần render ManagerName/ManagerEmail.
        /// </summary>
        Task<Cafe?> GetByIdWithManagerAsync(Guid id);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByIdAsync(Guid userId);
        Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null);
        Task AddCafeStaffAsync(CafeStaff cafeStaff);
        Task AddUserAsync(User user);
        Task<bool> IsStaffMemberExistsAsync(Guid cafeId, Guid userId);
        /// <summary>
        /// GAP-C1: True if userId is the cafe's manager OR a staff member —
        /// single-tenant authorization check for cross-cafe IDOR guards.
        /// </summary>
        Task<bool> IsManagerOrStaffAsync(Guid cafeId, Guid userId);
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
            string? name,
            PaginationParams paginationParams);

        Task<PaginatedResponse<NearbyCafeDto>> SearchCafesAsync(
            string name,
            double? latitude,
            double? longitude,
            double? radiusKm,
            PaginationParams paginationParams);

        /// <summary>
        /// Lấy tất cả quán đang ACTIVE (IsActive=true, PartnerOperationalStatus=Active), không filter Location.
        /// Sắp xếp theo Name A→Z. Trả về shape <see cref="NearbyCafeDto"/> (giống /nearby) để player thấy
        /// được AvailableGameCount/TotalGameBoxCount/AvailableTableCount/TotalTableCount.
        /// </summary>
        Task<PaginatedResponse<NearbyCafeDto>> GetAllActiveCafesAsync(
            PaginationParams paginationParams);
        Task<List<Cafe>> GetNearbyCafesAsync(Guid excludeCafeId, double radiusKm);
        Task EnrichNearbyWithGameWaitAsync(IList<NearbyCafeDto> cafes, Guid gameTemplateId);
        Task<IReadOnlyList<NearbyAlternativeGameSuggestionDto>> GetAlternativeGameSuggestionsAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid gameTemplateId,
            int limit = 10);
        Task<Cafe?> GetPartnerCafeByManagerIdAsync(Guid managerUserId);
        Task SyncCafeTablesAsync(Guid cafeId, IReadOnlyList<string> tableNames);
        /// <summary>
        /// Overload — đồng bộ cả Name + SeatCount + SortOrder.
        /// </summary>
        Task SyncCafeTablesAsync(Guid cafeId, IReadOnlyList<CafeTableSyncItem> tables);
        Task RefreshTableLayoutJsonAsync(Guid cafeId);
        Task SaveChangesAsync();

        // === Admin: Full CRUD ===
        Task AddCafeAsync(Cafe cafe);
        Task<(IReadOnlyList<Cafe> Items, int TotalCount)> GetAdminListAsync(
            int page, int pageSize, string? searchTerm, bool? isActive, Guid? managerId);
        Task<Cafe?> GetAdminDetailAsync(Guid cafeId);
        Task<int> CountAllAsync();
        Task<int> CountActiveAsync();

        // === Cafe Detail (extended public info) ===
        /// <summary>
        /// Lấy cafe với đầy đủ thông tin cho player: seat availability, refund policy, schedule overrides.
        /// Không yêu cầu auth.
        /// </summary>
        Task<Cafe?> GetCafeDetailAsync(Guid id);

        /// <summary>
        /// Lấy seat inventory cho cafe + date + timeSlots.
        /// </summary>
        Task<Dictionary<TimeSlot, int>> GetAvailableSeatsByTimeSlotAsync(Guid cafeId, DateOnly playDate);

        /// <summary>
        /// Lấy danh sách schedule overrides cho cafe.
        /// </summary>
        Task<List<CafeScheduleOverride>> GetScheduleOverridesAsync(Guid cafeId, DateOnly? fromDate = null, DateOnly? toDate = null);

        /// <summary>
        /// Đếm tổng held seats (reservations đang active) cho cafe trong ngày.
        /// </summary>
        Task<int> CountHeldSeatsAsync(Guid cafeId, DateOnly playDate);

        /// <summary>
        /// Đếm tổng in-use seats (active sessions) cho cafe trong ngày.
        /// </summary>
        Task<int> CountInUseSeatsAsync(Guid cafeId, DateOnly playDate);
    }
}
