using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ICafeSettlementRepository
    {
        Task AddAsync(CafeSettlement settlement, CancellationToken cancellationToken = default);
        Task UpdateAsync(CafeSettlement settlement, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CafeSettlement>> GetPendingAsync(Guid cafeId, CancellationToken cancellationToken = default);

        /// <summary>Get all settlements with Status=Failed (for retry job).</summary>
        Task<IReadOnlyList<CafeSettlement>> GetRetryableAsync(int maxAttempts, TimeSpan minRetryDelay, CancellationToken cancellationToken = default);

        /// <summary>W-06: Get settlement by Id for admin override.</summary>
        Task<CafeSettlement?> GetByIdAsync(Guid settlementId, CancellationToken cancellationToken = default);

        /// <summary>
        /// W-06: Admin list settlements với filter + phân trang.
        /// </summary>
        Task<PaginatedResponse<SettlementListItemDto>> GetPagedAsync(SettlementListQuery query, CancellationToken cancellationToken = default);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}