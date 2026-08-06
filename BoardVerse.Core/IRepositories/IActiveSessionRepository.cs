using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories
{
    public interface IActiveSessionRepository
    {
        Task<ActiveSession?> GetByIdAsync(Guid sessionId);
        Task<ActiveSession?> GetByIdWithMembersAsync(Guid sessionId);
        Task<ActiveSession?> GetByLobbyIdWithMembersAsync(Guid lobbyId);
        Task<ActiveSession?> GetByOrderIdAsync(string orderId);
        Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId);
        /// <summary>Returns all non-Paid sessions for seat calculation.</summary>
        Task<int> CountActiveSessionMembersAsync(Guid cafeId);

        /// <summary>
        /// Batch query: đếm số active session members cho N cafes trong 1 round-trip DB.
        /// Trả về Dictionary&lt;cafeId, count&gt;. Dùng cho ActiveSessionService.GetAlternativeCafesAsync
        /// để tránh N+1 (loop qua từng cafe → 1 query per cafe).
        /// </summary>
        Task<IReadOnlyDictionary<Guid, int>> CountActiveSessionMembersByCafesAsync(IReadOnlyCollection<Guid> cafeIds);
        Task<ActiveSessionMember?> GetMemberByIdAsync(Guid memberId);
        Task AddAsync(ActiveSession session);
        Task AddMemberAsync(ActiveSessionMember member);
        Task UpdateMemberAsync(ActiveSessionMember member);
        Task UpdateAsync(ActiveSession session);
        Task SaveChangesAsync();

        /// <summary>H8: Bắt đầu transaction cho PaySessionAsync atomicity (billing + cleanup + capture).</summary>
        Task<IDatabaseTransactionContext> BeginTransactionAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ActiveSession>> GetAllUnpaidAsync();

        // === Atomic status update for race condition prevention ===
        /// <summary>
        /// P0 Fix #2: Atomically updates status only if current status matches expected.
        /// Returns true if update succeeded (rows affected > 0).
        /// </summary>
        Task<bool> TryUpdateStatusAsync(Guid sessionId, GroupSessionStatus expectedStatus, GroupSessionStatus newStatus);

        // === Post-payment lifecycle cleanup ===
        /// <summary>
        /// Completes the post-payment lifecycle cleanup for a paid session.
        /// Marks all members as checked out, releases the board game box and cafe table,
        /// and closes any linked lobby. Idempotent: safe to call multiple times.
        /// </summary>
        Task CompleteSessionPaymentCleanupAsync(Guid sessionId);

        // === BVC Capture Retry (GAP-9) ===
        /// <summary>
        /// Returns sessions that are Paid but haven't had BVC captured yet.
        /// Used by background job to retry failed captures.
        /// </summary>
        Task<IReadOnlyList<ActiveSession>> GetSessionsNeedingBvcCaptureRetryAsync(int batchSize);

        // === Game checklist (BR-12) ===
        Task<ActiveSessionGame?> GetSessionGameByIdAsync(Guid sessionGameId);
        Task UpdateSessionGameAsync(ActiveSessionGame sessionGame);

        /// <summary>
        /// R-Bug-026 Fix: kiểm tra user có phải participant của session không
        /// (Host, member, hoặc staff check-in).
        /// </summary>
        Task<bool> IsUserSessionParticipantAsync(Guid sessionId, Guid userId);
    }
}