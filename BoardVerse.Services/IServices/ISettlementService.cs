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
        Task<IReadOnlyList<CafeSettlement>> GetPendingSettlementsAsync(Guid cafeId);
    }
}
