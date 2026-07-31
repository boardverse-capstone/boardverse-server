using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories
{
    public interface IActiveSessionRepository
    {
        Task<ActiveSession?> GetByIdAsync(Guid sessionId);
        Task<ActiveSession?> GetByIdWithMembersAsync(Guid sessionId);
        Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId);
        /// <summary>Returns all non-Paid sessions for seat calculation.</summary>
        Task<int> CountActiveSessionMembersAsync(Guid cafeId);
        Task<ActiveSessionMember?> GetMemberByIdAsync(Guid memberId);
        Task AddAsync(ActiveSession session);
        Task AddMemberAsync(ActiveSessionMember member);
        Task UpdateMemberAsync(ActiveSessionMember member);
        Task UpdateAsync(ActiveSession session);
        Task SaveChangesAsync();
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

        // === Game checklist (BR-12) ===
        Task<ActiveSessionGame?> GetSessionGameByIdAsync(Guid sessionGameId);
        Task UpdateSessionGameAsync(ActiveSessionGame sessionGame);
    }
}