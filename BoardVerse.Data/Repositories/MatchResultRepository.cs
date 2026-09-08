using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class MatchResultRepository : IMatchResultRepository
    {
        private static readonly Guid[] CompetitiveCategoryIds =
        [
            CategoryConfiguration.CompetitiveId,
            CategoryConfiguration.StrategyId
        ];

        private readonly BoardVerseDbContext _context;

        public MatchResultRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Load lobby + members + game template cho MatchResult flow.
        /// 
        /// GAP-1 fix: KHÔNG filter Members.Where(IsActive) — sau khi POS đóng phiên,
        /// ReservationService.MarkLobbyMembersInactive set IsActive=false cho TẤT CẢ members
        /// (Status = LobbyTerminated) để giải phóng BR-USER-LIMIT-* check. Filter cũ khiến
        /// collection rỗng → RequireEligibleLobbyAsync throw 403 cho cả host lẫn members.
        /// 
        /// MatchResult validation giờ dựa vào LobbyMemberStatus (Kicked/Left = thật sự rời)
        /// thay vì IsActive flag (chỉ là audit cho BR-USER-LIMIT-*).
        /// </summary>
        public Task<Lobby?> GetLobbyForMatchAsync(Guid lobbyId, CancellationToken cancellationToken = default) =>
            _context.Lobbies
                .AsNoTracking()
                .Include(l => l.Members)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .Include(l => l.GameTemplate)
                .FirstOrDefaultAsync(l => l.Id == lobbyId);

        public Task<bool> GameSupportsMatchResultsAsync(Guid gameTemplateId, CancellationToken cancellationToken = default) =>
            _context.GameTemplateCategories
                .AsNoTracking()
                .AnyAsync(gtc =>
                    gtc.GameTemplateId == gameTemplateId
                    && CompetitiveCategoryIds.Contains(gtc.CategoryId));

        public Task<MatchResult?> GetSubmissionAsync(Guid lobbyId, Guid userId, CancellationToken cancellationToken = default) =>
            _context.MatchResults
                .FirstOrDefaultAsync(r => r.LobbyId == lobbyId && r.UserId == userId);

        public async Task<IReadOnlyList<MatchResult>> GetSubmissionsAsync(Guid lobbyId, CancellationToken cancellationToken = default) =>
            await _context.MatchResults
                .AsNoTracking()
                .Where(r => r.LobbyId == lobbyId)
                .ToListAsync();

        public Task<MatchHistory?> GetFinalizedHistoryAsync(Guid lobbyId, CancellationToken cancellationToken = default) =>
            _context.MatchHistories
                .AsNoTracking()
                .Include(h => h.Participants)
                .FirstOrDefaultAsync(h => h.LobbyId == lobbyId);

        public Task AddSubmissionAsync(MatchResult submission, CancellationToken cancellationToken = default)
        {
            _context.MatchResults.Add(submission);
            return Task.CompletedTask;
        }

        public Task AddMatchHistoryAsync(MatchHistory history, CancellationToken cancellationToken = default)
        {
            _context.MatchHistories.Add(history);
            return Task.CompletedTask;
        }

        public Task<UserProfile?> GetProfileForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _context.SaveChangesAsync();
    }
}
