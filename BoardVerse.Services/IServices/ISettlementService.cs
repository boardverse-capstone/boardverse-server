using BoardVerse.Core.Entities;

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
            Guid activeSessionId);
        Task<IReadOnlyList<CafeSettlement>> GetPendingSettlementsAsync(Guid cafeId, Guid actorUserId, string actorRole);

        /// <summary>
        /// W-06: Admin manually override a failed settlement after retry exhaustion.
        /// Sets Status = Overridden, OverrideBy = adminId, OverrideAt = now.
        /// </summary>
        Task<CafeSettlement> OverrideSettlementAsync(Guid settlementId, Guid adminUserId);
    }
}
