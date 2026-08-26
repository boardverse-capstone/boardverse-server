using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ICafePosRepository
    {
        Task<bool> CanOperateCafeAsync(Guid cafeId, Guid userId, string userRole, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CafeTable>> GetActiveTablesAsync(Guid cafeId, bool includeInactive = false, CancellationToken cancellationToken = default);
        Task<CafeTable?> GetTableAsync(Guid cafeId, Guid tableId, CancellationToken cancellationToken = default);
        Task UpdateTableAsync(CafeTable table, CancellationToken cancellationToken = default);
        Task<bool> HasActiveSessionForTableAsync(Guid cafeId, Guid tableId, CancellationToken cancellationToken = default);
        Task<CafeInventoryBox?> GetBoxByBarcodeAsync(Guid cafeId, string barcode, CancellationToken cancellationToken = default);
        Task<CafeInventoryBox?> GetInventoryBoxByIdAsync(Guid boxId, CancellationToken cancellationToken = default);
        Task UpdateInventoryBoxAsync(CafeInventoryBox box, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CafeInventoryBox>> GetBoxesAsync(Guid cafeId, Guid? gameTemplateId, CancellationToken cancellationToken = default);
        Task<ActiveSession?> GetActiveSessionByIdAsync(Guid cafeId, Guid sessionId, CancellationToken cancellationToken = default);
        Task<ActiveSession?> GetActiveSessionByBoxIdAsync(Guid boxId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lightweight check: session có tồn tại và thuộc cafe này không (không load navigation).
        /// Dùng cho cross-cafe guard khi truyền optional sessionId.
        /// </summary>
        Task<bool> ActiveSessionExistsInCafeAsync(Guid sessionId, Guid cafeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách CafeTableId hiện đang có session chưa thanh toán (Active / Checking / Unpaid).
        /// Dùng để derive trạng thái bàn trong <c>GetTablesAsync</c> — đảm bảo bàn có session hoạt động
        /// luôn trả về <see cref="Core.Enum.CafeTableStatus.InUse"/> bất chấp giá trị <c>CafeTables.Status</c>
        /// trong DB có bị stale hay không (ví dụ do migration / manual fixup / bug trước đó).
        ///
        /// Trả kèm <c>Status</c> của session đang đầu tiên để service biết session "quan trọng nhất"
        /// (ưu tiên Active &gt; Checking &gt; Unpaid) cho trường hợp cần show trong UI.
        /// </summary>
        Task<IReadOnlyDictionary<Guid, GroupSessionStatus>> GetBusyTableIdsByCafeAsync(Guid cafeId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ActiveSession>> GetUnpaidSessionsAsync(Guid cafeId, Guid? sessionId = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Đếm số thành viên cho nhiều session trong 1 query (tránh N+1).
        /// Trả Dictionary&lt;SessionId, Count&gt;. Session nào không có member → count = 0.
        /// </summary>
        Task<IReadOnlyDictionary<Guid, int>> GetActiveSessionMemberCountsAsync(Guid cafeId, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default);

        Task<PaidSessionsPagedResult> GetPaidSessionsPagedAsync(Guid cafeId, DateOnly fromDate, DateOnly toDate, Guid? gameTemplateId, Guid? staffId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<ActiveSessionGame?> GetActiveSessionGameByIdAsync(Guid sessionGameId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ActiveSessionGame>> GetSessionGamesAsync(Guid sessionId, CancellationToken cancellationToken = default);
        Task<bool> IsSessionFullyCheckedAsync(Guid sessionId, CancellationToken cancellationToken = default);

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
            Guid? sessionId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// FIX Bug: Lấy kết quả kiểm kê MỚI NHẤT cho 1 box (theo ActiveSessionGame.CheckedAt DESC).
        /// Trả Dictionary&lt;componentId, ComponentCheckResult&gt; để tra nhanh ActualQuantity gần nhất.
        /// Dùng cho GetComponentChecklistAsync: nếu box từng bị mất linh kiện ở phiên trước → dùng số lượng
        /// thực tế còn lại làm ExpectedQuantity mới (snapshot baseline mới).
        /// </summary>
        Task<IReadOnlyDictionary<Guid, ComponentCheckResult>> GetLatestComponentCheckByBoxAsync(Guid boxId, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetGameTemplateWithComponentsAsync(Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<CafeGameComponentPenalty?> GetComponentPenaltyAsync(Guid cafeId, Guid gameTemplateId, Guid componentId, CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<Guid, CafeGameComponentPenalty>> GetComponentPenaltiesByCafeGameAsync(
            Guid cafeId, Guid gameTemplateId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken = default);
        Task AddSessionAsync(ActiveSession session, CancellationToken cancellationToken = default);
        Task AddSessionMemberAsync(ActiveSessionMember member, CancellationToken cancellationToken = default);
        Task AddSessionGameAsync(ActiveSessionGame sessionGame, CancellationToken cancellationToken = default);
        Task AddComponentLossReportAsync(ComponentLossReport report, CancellationToken cancellationToken = default);

        /// <summary>BR-12: Insert bộ kết quả kiểm kê chi tiết (mỗi component 1 dòng).</summary>
        Task AddComponentCheckResultsAsync(IEnumerable<ComponentCheckResult> results, CancellationToken cancellationToken = default);

        /// <summary>BR-12: Xóa kết quả kiểm kê cũ khi staff reset checklist.</summary>
        Task DeleteComponentCheckResultsAsync(Guid activeSessionGameId, CancellationToken cancellationToken = default);

        Task UpdateDepositAsync(BookingDeposit deposit, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        // GAP-1/GAP-37 Fix: Idempotency + Nonce tracking
        Task<ActiveSession?> GetSessionByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
        Task SaveIdempotencyKeyAsync(Guid sessionId, string idempotencyKey, CancellationToken cancellationToken = default);
        Task<bool> IsNonceUsedAsync(string nonce, CancellationToken cancellationToken = default);
        Task MarkNonceUsedAsync(string nonce, CancellationToken cancellationToken = default);
    }
}
