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

        /// <summary>
        /// W-07: Lấy tất cả settlement trong 1 ngày (UTC range) — kèm CafeName join.
        /// Settlement được coi là thuộc ngày X nếu:
        ///   - Status = Succeeded/Overridden: <c>TransferredAt</c> nằm trong range (hoặc fallback <c>CreatedAt</c>).
        ///   - Status = Pending/Failed/Retrying: <c>CreatedAt</c> nằm trong range.
        /// Repository trả list raw, Service sẽ group/aggregate theo cafe.
        /// </summary>
        Task<IReadOnlyList<CafeSettlement>> GetForDailySummaryAsync(
            DateTime startUtcInclusive,
            DateTime endUtcExclusive,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}