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

        public Task<Lobby?> GetLobbyForMatchAsync(Guid lobbyId) =>
            _context.Lobbies
                .AsNoTracking()
                .Include(l => l.Members.Where(m => m.IsActive))
                    .ThenInclude(m => m.User)
                .Include(l => l.GameTemplate)
                .FirstOrDefaultAsync(l => l.Id == lobbyId);

        public Task<bool> IsActiveLobbyMemberAsync(Guid lobbyId, Guid userId) =>
            _context.LobbyMembers
                .AsNoTracking()
                .AnyAsync(m => m.LobbyId == lobbyId && m.UserId == userId && m.IsActive);

        public Task<bool> GameSupportsMatchResultsAsync(Guid gameTemplateId) =>
            _context.GameTemplateCategories
                .AsNoTracking()
                .AnyAsync(gtc =>
                    gtc.GameTemplateId == gameTemplateId
                    && CompetitiveCategoryIds.Contains(gtc.CategoryId));

        public Task<MatchResult?> GetSubmissionAsync(Guid lobbyId, Guid userId) =>
            _context.MatchResults
                .FirstOrDefaultAsync(r => r.LobbyId == lobbyId && r.UserId == userId);

        public async Task<IReadOnlyList<MatchResult>> GetSubmissionsAsync(Guid lobbyId) =>
            await _context.MatchResults
                .AsNoTracking()
                .Where(r => r.LobbyId == lobbyId)
                .ToListAsync();

        public Task<MatchHistory?> GetFinalizedHistoryAsync(Guid lobbyId) =>
            _context.MatchHistories
                .AsNoTracking()
                .Include(h => h.Participants)
                .FirstOrDefaultAsync(h => h.LobbyId == lobbyId);

        public Task AddSubmissionAsync(MatchResult submission)
        {
            _context.MatchResults.Add(submission);
            return Task.CompletedTask;
        }

        public Task AddMatchHistoryAsync(MatchHistory history)
        {
            _context.MatchHistories.Add(history);
            return Task.CompletedTask;
        }

        public Task<UserProfile?> GetProfileForUpdateAsync(Guid userId) =>
            _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
