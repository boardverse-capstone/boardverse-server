using BoardVerse.Core.DTOs.Pos;
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

        /// <summary>
        /// Lightweight check: session có tồn tại và thuộc cafe này không (không load navigation).
        /// Dùng cho cross-cafe guard khi truyền optional sessionId.
        /// </summary>
        Task<bool> ActiveSessionExistsInCafeAsync(Guid sessionId, Guid cafeId);
        Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId);
        Task<IReadOnlyList<ActiveSession>> GetUnpaidSessionsAsync(Guid cafeId, Guid? sessionId = null);
        /// <summary>
        /// Đếm số thành viên cho nhiều session trong 1 query (tránh N+1).
        /// Trả Dictionary&lt;SessionId, Count&gt;. Session nào không có member → count = 0.
        /// </summary>
        Task<IReadOnlyDictionary<Guid, int>> GetActiveSessionMemberCountsAsync(Guid cafeId, IReadOnlyCollection<Guid> sessionIds);

        Task<PaidSessionsPagedResult> GetPaidSessionsPagedAsync(Guid cafeId, DateOnly fromDate, DateOnly toDate, Guid? gameTemplateId, Guid? staffId, int pageNumber, int pageSize);
        Task<ActiveSessionGame?> GetActiveSessionGameByIdAsync(Guid sessionGameId);
        Task<IReadOnlyList<ActiveSessionGame>> GetSessionGamesAsync(Guid sessionId);
        Task<bool> IsSessionFullyCheckedAsync(Guid sessionId);

        /// <summary>
        /// Box history #1: Lấy tất cả <c>ActiveSessionGame</c> thuộc một hộp cụ thể
        /// đã được kiểm kê với trạng thái <c>MissingComponents</c>.
        /// Trả kèm navigation: ComponentCheckResults + GameComponentTemplate + Staff + Members.
        /// Sắp xếp theo CheckedAt DESC (mới nhất trước).
        /// </summary>
        /// <param name="boxId">Mã hộp game (CafeInventoryBox).</param>
        /// <param name="sessionId">
        /// Optional: nếu truyền, chỉ trả incidents thuộc <c>ActiveSessionId</c> này.
        /// Nếu null/empty Guid, trả tất cả incidents của hộp (audit mode).
        /// </param>
        Task<IReadOnlyList<ActiveSessionGame>> GetMissingComponentIncidentsByBoxAsync(
            Guid boxId,
            Guid? sessionId = null);

        /// <summary>
        /// FIX Bug: Lấy kết quả kiểm kê MỚI NHẤT cho 1 box (theo ActiveSessionGame.CheckedAt DESC).
        /// Trả Dictionary&lt;componentId, ComponentCheckResult&gt; để tra nhanh ActualQuantity gần nhất.
        /// Dùng cho GetComponentChecklistAsync: nếu box từng bị mất linh kiện ở phiên trước → dùng số lượng
        /// thực tế còn lại làm ExpectedQuantity mới (snapshot baseline mới).
        /// </summary>
        Task<IReadOnlyDictionary<Guid, ComponentCheckResult>> GetLatestComponentCheckByBoxAsync(Guid boxId);
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
