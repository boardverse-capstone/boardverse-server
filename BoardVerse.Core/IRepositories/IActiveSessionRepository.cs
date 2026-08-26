using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IActiveSessionRepository
    {
    Task<ActiveSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<ActiveSession?> GetByIdWithMembersAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<ActiveSession?> GetByLobbyIdWithMembersAsync(Guid lobbyId, CancellationToken cancellationToken = default);
    Task<ActiveSession?> GetByUserIdWithMembersAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActiveSession>> GetHistoryByUserIdAsync(Guid userId, int limit = 20, DateTime? beforePaidAt = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<List<ActiveSession>> GetActiveSessionsInRangeAsync(Guid cafeId, DateTime rangeStart, DateTime rangeEnd, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActiveSession>> GetExpiredForUpdateAsync(DateTime cutoff, CancellationToken ct = default);
    Task<bool> IsUserSessionParticipantInCafeAsync(Guid sessionId, Guid userId, Guid cafeId, CancellationToken cancellationToken = default);
    Task<ActiveSession?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Split Bill (2026-08-25): Lookup session qua MemberId — dùng cho webhook QR
    /// khi SePay trả về OrderId chỉ chứa memberId (không có sessionId).
    /// Trả ActiveSession cùng .Members + .Cafe + navigation cần thiết.
    /// </summary>
    Task<ActiveSession?> GetByMemberIdWithSessionAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-END-05: Lấy session Active mà ExtendedEndTime (hoặc ScheduledEndTime) + grace 30 phút đã qua.
    /// Dùng cho AutoReleaseExpiredSessionsJob.
    /// </summary>
    Task<IReadOnlyList<ActiveSession>> GetExpiredAsync(DateTime cutoff, CancellationToken ct = default);
        /// <summary>Returns all non-Paid sessions for seat calculation.</summary>
        Task<int> CountActiveSessionMembersAsync(Guid cafeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Batch query: đếm số active session members cho N cafes trong 1 round-trip DB.
        /// Trả về Dictionary&lt;cafeId, count&gt;. Dùng cho ActiveSessionService.GetAlternativeCafesAsync
        /// để tránh N+1 (loop qua từng cafe → 1 query per cafe).
        /// </summary>
        Task<IReadOnlyDictionary<Guid, int>> CountActiveSessionMembersByCafesAsync(IReadOnlyCollection<Guid> cafeIds, CancellationToken cancellationToken = default);
        Task<ActiveSessionMember?> GetMemberByIdAsync(Guid memberId, CancellationToken cancellationToken = default);
        Task AddAsync(ActiveSession session, CancellationToken cancellationToken = default);
        Task AddMemberAsync(ActiveSessionMember member, CancellationToken cancellationToken = default);
        Task UpdateMemberAsync(ActiveSessionMember member, CancellationToken cancellationToken = default);
        Task UpdateAsync(ActiveSession session, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>H8: Bắt đầu transaction cho PaySessionAsync atomicity (billing + cleanup + capture).</summary>
        Task<IDatabaseTransactionContext> BeginTransactionAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ActiveSession>> GetAllUnpaidAsync(CancellationToken cancellationToken = default);

        // === Atomic status update for race condition prevention ===
        /// <summary>
        /// P0 Fix #2: Atomically updates status only if current status matches expected.
        /// Returns true if update succeeded (rows affected > 0).
        /// </summary>
        Task<bool> TryUpdateStatusAsync(Guid sessionId, GroupSessionStatus expectedStatus, GroupSessionStatus newStatus, CancellationToken cancellationToken = default);

        // === Post-payment lifecycle cleanup ===
        /// <summary>
        /// Marks all members as checked out and closes any linked lobby.
        /// Called at checkout time (when session becomes UNPAID).
        /// Table/box release is handled separately in ReleaseSessionTableAndBoxAsync.
        /// Idempotent: safe to call multiple times.
        /// </summary>
        Task ReleaseMembersAndCloseLobbyAsync(Guid sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases the board game box and cafe table back to Available.
        /// Called at payment time (when session becomes PAID) and by auto-release job.
        /// Idempotent: safe to call multiple times.
        /// </summary>
        Task ReleaseSessionTableAndBoxAsync(Guid sessionId, CancellationToken cancellationToken = default);

        // === BVC Capture Retry (GAP-9) ===
        /// <summary>
        /// Returns sessions that are Paid but haven't had BVC captured yet.
        /// Used by background job to retry failed captures.
        /// </summary>
        Task<IReadOnlyList<ActiveSession>> GetSessionsNeedingBvcCaptureRetryAsync(int batchSize, CancellationToken cancellationToken = default);

        // === Game checklist (BR-12) ===
        Task<ActiveSessionGame?> GetSessionGameByIdAsync(Guid sessionGameId, CancellationToken cancellationToken = default);
        Task UpdateSessionGameAsync(ActiveSessionGame sessionGame, CancellationToken cancellationToken = default);

        /// <summary>
        /// R-Bug-026 Fix: kiểm tra user có phải participant của session không
        /// (Host, member, hoặc staff check-in).
        /// </summary>
        Task<bool> IsUserSessionParticipantAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
    }
}