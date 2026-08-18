using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafeSettlementRepository
    {
        Task AddAsync(CafeSettlement settlement);
        Task UpdateAsync(CafeSettlement settlement);
        Task<IReadOnlyList<CafeSettlement>> GetPendingAsync(Guid cafeId);

        /// <summary>Get all settlements with Status=Failed (for retry job).</summary>
        Task<IReadOnlyList<CafeSettlement>> GetRetryableAsync(int maxAttempts, TimeSpan minRetryDelay);

        /// <summary>W-06: Get settlement by Id for admin override.</summary>
        Task<CafeSettlement?> GetByIdAsync(Guid settlementId);

        /// <summary>
        /// W-06: Admin list settlements với filter + phân trang.
        /// </summary>
        Task<PaginatedResponse<SettlementListItemDto>> GetPagedAsync(SettlementListQuery query);

        Task SaveChangesAsync();
    }
}