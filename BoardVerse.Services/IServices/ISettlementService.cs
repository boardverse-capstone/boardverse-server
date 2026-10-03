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

        /// <summary>
        /// W-07: Tổng hợp giải ngân theo NGÀY (mặc định hôm nay theo giờ VN UTC+7).
        /// Dùng cho màn hình admin: "Hôm nay chuyển bao nhiêu tiền cho quán nào".
        ///
        /// <para>
        /// Quy ước ngày:
        /// </para>
        /// <list type="bullet">
        ///   <item>Nếu <paramref name="date"/> null → dùng ngày hiện tại theo <see cref="BoardVerse.Core.Constants.CafeSchedule.VietnamTz"/>.</item>
        ///   <item>UTC range = [date 00:00:00 VN, (date+1) 00:00:00 VN).</item>
        ///   <item>Settlement thuộc ngày X nếu TransferredAt (Succeeded/Overridden) hoặc CreatedAt (Pending/Retrying/Failed) nằm trong range.</item>
        /// </list>
        /// </summary>
        Task<SettlementDailySummaryDto> GetDailySummaryAsync(
            DateOnly? date = null,
            CancellationToken cancellationToken = default);
    }
}
