using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;
using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ICafeRepository
    {
        Task<Cafe?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Cafe?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Cafe?> GetByIdWithInventoriesAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy Cafe kèm Manager navigation (FK User) — dùng cho Admin GET /api/admin/cafes/{id}
        /// cần render ManagerName/ManagerEmail.
        /// </summary>
        Task<Cafe?> GetByIdWithManagerAsync(Guid id, CancellationToken cancellationToken = default);
        Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);
        Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<bool> UsernameExistsAsync(string username, Guid? excludedUserId = null, CancellationToken cancellationToken = default);
        Task AddCafeStaffAsync(CafeStaff cafeStaff, CancellationToken cancellationToken = default);
        Task AddUserAsync(User user, CancellationToken cancellationToken = default);
        Task<bool> IsStaffMemberExistsAsync(Guid cafeId, Guid userId, CancellationToken cancellationToken = default);
        /// <summary>
        /// GAP-C1: True if userId is the cafe's manager OR a staff member —
        /// single-tenant authorization check for cross-cafe IDOR guards.
        /// </summary>
        Task<bool> IsManagerOrStaffAsync(Guid cafeId, Guid userId, CancellationToken cancellationToken = default);
        Task<int> CountActiveStaffAssignmentsAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<StaffDto>> GetStaffPagedAsync(Guid cafeId, PaginationParams paginationParams, CancellationToken cancellationToken = default);
        Task<CafeStaff?> GetCafeStaffAsync(Guid cafeId, Guid staffId, CancellationToken cancellationToken = default);
        Task RemoveCafeStaffAsync(CafeStaff cafeStaff, CancellationToken cancellationToken = default);
        Task<IEnumerable<Cafe>> GetCafesByStaffIdAsync(Guid staffId, CancellationToken cancellationToken = default);
        Task<IEnumerable<Cafe>> GetCafesByManagerIdAsync(Guid managerId, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<NearbyCafeDto>> GetNearbyAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid? gameTemplateId,
            string? name,
            PaginationParams paginationParams, CancellationToken cancellationToken = default);

        Task<PaginatedResponse<NearbyCafeDto>> SearchCafesAsync(
            string name,
            double? latitude,
            double? longitude,
            double? radiusKm,
            PaginationParams paginationParams, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy tất cả quán đang ACTIVE (IsActive=true, PartnerOperationalStatus=Active), không filter Location.
        /// Sắp xếp theo Name A→Z. Trả về shape <see cref="NearbyCafeDto"/> (giống /nearby) để player thấy
        /// được AvailableGameCount/TotalGameBoxCount/AvailableTableCount/TotalTableCount.
        /// </summary>
        Task<PaginatedResponse<NearbyCafeDto>> GetAllActiveCafesAsync(
            PaginationParams paginationParams, CancellationToken cancellationToken = default);
        Task<List<Cafe>> GetNearbyCafesAsync(Guid excludeCafeId, double radiusKm, CancellationToken cancellationToken = default);
        Task EnrichNearbyWithGameWaitAsync(IList<NearbyCafeDto> cafes, Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<NearbyAlternativeGameSuggestionDto>> GetAlternativeGameSuggestionsAsync(
            double latitude,
            double longitude,
            double radiusKm,
            Guid gameTemplateId,
            int limit = 10, CancellationToken cancellationToken = default);
        Task<Cafe?> GetPartnerCafeByManagerIdAsync(Guid managerUserId, CancellationToken cancellationToken = default);
        Task SyncCafeTablesAsync(Guid cafeId, IReadOnlyList<string> tableNames, CancellationToken cancellationToken = default);
        /// <summary>
        /// Overload — đồng bộ cả Name + SeatCount + SortOrder.
        /// </summary>
        Task SyncCafeTablesAsync(Guid cafeId, IReadOnlyList<CafeTableSyncItem> tables, CancellationToken cancellationToken = default);
        Task RefreshTableLayoutJsonAsync(Guid cafeId, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        // === Admin: Full CRUD ===
        Task AddCafeAsync(Cafe cafe, CancellationToken cancellationToken = default);
        Task<(IReadOnlyList<Cafe> Items, int TotalCount)> GetAdminListAsync(
            int page, int pageSize, string? searchTerm, bool? isActive, Guid? managerId, CancellationToken cancellationToken = default);
        Task<Cafe?> GetAdminDetailAsync(Guid cafeId, CancellationToken cancellationToken = default);
        Task<int> CountAllAsync(CancellationToken cancellationToken = default);
        Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

        // === Cafe Detail (extended public info) ===
        /// <summary>
        /// Lấy cafe với đầy đủ thông tin cho player: seat availability, refund policy, schedule overrides.
        /// Không yêu cầu auth.
        /// </summary>
        Task<Cafe?> GetCafeDetailAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy seat inventory cho cafe + date + timeSlots.
        /// </summary>
        Task<Dictionary<TimeSlot, int>> GetAvailableSeatsByTimeSlotAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách schedule overrides cho cafe.
        /// </summary>
        Task<List<CafeScheduleOverride>> GetScheduleOverridesAsync(Guid cafeId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Đếm tổng held seats (reservations đang active) cho cafe trong ngày.
        /// </summary>
        Task<int> CountHeldSeatsAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Đếm tổng in-use seats (active sessions) cho cafe trong ngày.
        /// </summary>
        Task<int> CountInUseSeatsAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default);
    }
}
