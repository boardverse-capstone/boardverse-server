using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class ActiveSessionRepository : IActiveSessionRepository
    {
        private readonly BoardVerseDbContext _db;

        public ActiveSessionRepository(BoardVerseDbContext db)
        {
            _db = db;
        }

        public async Task<ActiveSession?> GetByIdAsync(Guid sessionId)
        {
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s => s.Id == sessionId);
        }

        public async Task<ActiveSession?> GetByIdWithMembersAsync(Guid sessionId)
        {
            return await _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .FirstOrDefaultAsync(s => s.Id == sessionId);
        }

        public async Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId)
        {
            var query = _db.ActiveSessions
                .Include(s => s.Members)
                    .ThenInclude(m => m.User)
                .Include(s => s.Games)
                    .ThenInclude(g => g.CafeInventoryBox)
                .Include(s => s.Games)
                    .ThenInclude(g => g.GameTemplate)
                .Include(s => s.CafeTable)
                .Include(s => s.CafeInventoryBox)
                .Include(s => s.GameTemplate)
                .Where(s => s.CafeId == cafeId && s.Status != GroupSessionStatus.Paid);

            if (gameTemplateId.HasValue)
            {
                query = query.Where(s => s.GameTemplateId == gameTemplateId.Value);
            }

            return await query.ToListAsync();
        }

        public Task AddAsync(ActiveSession session)
        {
            _db.ActiveSessions.Add(session);
            return Task.CompletedTask;
        }

        public Task AddMemberAsync(ActiveSessionMember member)
        {
            _db.ActiveSessionMembers.Add(member);
            return Task.CompletedTask;
        }

        public Task UpdateMemberAsync(ActiveSessionMember member)
        {
            _db.ActiveSessionMembers.Update(member);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ActiveSession session)
        {
            _db.ActiveSessions.Update(session);
            return Task.CompletedTask;
        }

        public async Task<int> CountActiveSessionMembersAsync(Guid cafeId)
        {
            return await _db.ActiveSessionMembers
                .Where(m => m.ActiveSession!.CafeId == cafeId
                    && m.ActiveSession.Status != GroupSessionStatus.Paid
                    && m.Status != IndividualSessionStatus.Finished)
                .CountAsync();
        }

        public async Task<ActiveSessionMember?> GetMemberByIdAsync(Guid memberId)
        {
            return await _db.ActiveSessionMembers
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == memberId);
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<ActiveSession>> GetAllUnpaidAsync()
        {
            return await _db.ActiveSessions
                .Where(s => s.Status == GroupSessionStatus.Unpaid && !string.IsNullOrWhiteSpace(s.OrderId))
                .ToListAsync();
        }
    }
}
