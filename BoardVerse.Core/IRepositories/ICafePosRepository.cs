using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafePosRepository
    {
        Task<bool> CanOperateCafeAsync(Guid cafeId, Guid userId, string userRole);
        Task<IReadOnlyList<CafeTable>> GetActiveTablesAsync(Guid cafeId, bool includeInactive = false);
        Task<CafeTable?> GetTableAsync(Guid cafeId, Guid tableId);
        Task UpdateTableAsync(CafeTable table);
        Task<bool> HasActiveSessionForTableAsync(Guid cafeId, Guid tableId);
        Task<CafeInventoryBox?> GetBoxByBarcodeAsync(Guid cafeId, string barcode);
        Task<CafeInventoryBox?> GetInventoryBoxByIdAsync(Guid boxId);
        Task UpdateInventoryBoxAsync(CafeInventoryBox box);
        Task<IReadOnlyList<CafeInventoryBox>> GetBoxesAsync(Guid cafeId, Guid? gameTemplateId);
        Task<ActiveSession?> GetActiveSessionByIdAsync(Guid cafeId, Guid sessionId);
        Task<ActiveSession?> GetActiveSessionByBoxIdAsync(Guid boxId);
        Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId);
        Task<ActiveSessionGame?> GetActiveSessionGameByIdAsync(Guid sessionGameId);
        Task<IReadOnlyList<ActiveSessionGame>> GetSessionGamesAsync(Guid sessionId);
        Task<bool> IsSessionFullyCheckedAsync(Guid sessionId);
        Task<GameTemplate?> GetGameTemplateWithComponentsAsync(Guid gameTemplateId);
        Task<CafeGameComponentPenalty?> GetComponentPenaltyAsync(Guid cafeId, Guid gameTemplateId, Guid componentId);
        Task<IReadOnlyDictionary<Guid, CafeGameComponentPenalty>> GetComponentPenaltiesByCafeGameAsync(
            Guid cafeId, Guid gameTemplateId, IReadOnlyCollection<Guid> componentIds);
        Task AddSessionAsync(ActiveSession session);
        Task AddSessionMemberAsync(ActiveSessionMember member);
        Task AddSessionGameAsync(ActiveSessionGame sessionGame);
        Task AddComponentLossReportAsync(ComponentLossReport report);

        /// <summary>BR-12: Insert bộ kết quả kiểm kê chi tiết (mỗi component 1 dòng).</summary>
        Task AddComponentCheckResultsAsync(IEnumerable<ComponentCheckResult> results);

        /// <summary>BR-12: Xóa kết quả kiểm kê cũ khi staff reset checklist.</summary>
        Task DeleteComponentCheckResultsAsync(Guid activeSessionGameId);

        Task UpdateDepositAsync(BookingDeposit deposit);
        Task SaveChangesAsync();

        // GAP-1/GAP-37 Fix: Idempotency + Nonce tracking
        Task<ActiveSession?> GetSessionByIdempotencyKeyAsync(string idempotencyKey);
        Task SaveIdempotencyKeyAsync(Guid sessionId, string idempotencyKey);
        Task<bool> IsNonceUsedAsync(string nonce);
        Task MarkNonceUsedAsync(string nonce);
    }
}
