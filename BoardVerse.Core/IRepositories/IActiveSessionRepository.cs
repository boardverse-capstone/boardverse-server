using BoardVerse.Core.Entities;

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

        // === Game checklist (BR-12) ===
        Task<ActiveSessionGame?> GetSessionGameByIdAsync(Guid sessionGameId);
        Task UpdateSessionGameAsync(ActiveSessionGame sessionGame);
    }
}