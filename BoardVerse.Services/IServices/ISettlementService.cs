using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices
{
    public interface ISettlementService
    {
        Task<CafeSettlement> ReleaseSessionDepositAsync(Guid cafeId, Guid sessionId, Guid activeSessionId);
        Task<IReadOnlyList<CafeSettlement>> GetPendingSettlementsAsync(Guid cafeId);
    }
}
