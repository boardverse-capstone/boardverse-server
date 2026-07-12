using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafePosRepository
    {
        Task<bool> CanOperateCafeAsync(Guid cafeId, Guid userId, string userRole);
        Task<IReadOnlyList<CafeTable>> GetActiveTablesAsync(Guid cafeId);
        Task<CafeTable?> GetTableAsync(Guid cafeId, Guid tableId);
        Task<CafeInventoryBox?> GetBoxByBarcodeAsync(Guid cafeId, string barcode);
        Task<IReadOnlyList<CafeInventoryBox>> GetBoxesAsync(Guid cafeId, Guid? gameTemplateId);
        Task<ActiveSession?> GetActiveSessionByIdAsync(Guid cafeId, Guid sessionId);
        Task<ActiveSession?> GetActiveSessionByBoxIdAsync(Guid boxId);
        Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId);
        Task<ActiveSessionGame?> GetActiveSessionGameByIdAsync(Guid sessionGameId);
        Task<IReadOnlyList<ActiveSessionGame>> GetSessionGamesAsync(Guid sessionId);
        Task<bool> IsSessionFullyCheckedAsync(Guid sessionId);
        Task<GameTemplate?> GetGameTemplateWithComponentsAsync(Guid gameTemplateId);
        Task<CafeGameComponentPenalty?> GetComponentPenaltyAsync(Guid cafeId, Guid gameTemplateId, Guid componentId);
        Task AddSessionAsync(ActiveSession session);
        Task AddSessionMemberAsync(ActiveSessionMember member);
        Task AddSessionGameAsync(ActiveSessionGame sessionGame);
        Task AddComponentLossReportAsync(ComponentLossReport report);
        Task UpdateDepositAsync(BookingDeposit deposit);
        Task SaveChangesAsync();
    }
}
