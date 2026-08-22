using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface ISettlementService
    {
        /// <summary>
        /// Release deposit của session vào tài khoản cafe.
        /// </summary>
        Task<CafeSettlement> ReleaseSessionDepositAsync(
            Guid cafeId,
            Guid sessionId,
            Guid activeSessionId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CafeSettlement>> GetPendingSettlementsAsync(Guid cafeId, Guid actorUserId, string actorRole);

        /// <summary>
        /// W-06: Admin list settlements với filter + phân trang.
        /// </summary>
        Task<PaginatedResponse<SettlementListItemDto>> GetPagedAsync(SettlementListQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// W-06: Admin manually override a failed settlement after retry exhaustion.
        /// Sets Status = Overridden, OverrideBy = adminId, OverrideAt = now.
        /// </summary>
        Task<CafeSettlement> OverrideSettlementAsync(Guid settlementId, Guid adminUserId, CancellationToken cancellationToken = default);
    }
}
