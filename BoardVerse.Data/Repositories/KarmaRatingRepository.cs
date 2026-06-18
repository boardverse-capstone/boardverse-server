using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class KarmaRatingRepository : IKarmaRatingRepository
    {
        private readonly BoardVerseDbContext _context;

        public KarmaRatingRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task<Lobby?> GetLobbyForRatingAsync(Guid lobbyId) =>
            _context.Lobbies
                .AsNoTracking()
                .Include(l => l.Members.Where(m => m.IsActive))
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Profile)
                .FirstOrDefaultAsync(l => l.Id == lobbyId);

        public Task<Lobby?> GetLobbyForUpdateAsync(Guid lobbyId) =>
            _context.Lobbies
                .Include(l => l.Members.Where(m => m.IsActive))
                .FirstOrDefaultAsync(l => l.Id == lobbyId);

        public Task<bool> IsActiveLobbyMemberAsync(Guid lobbyId, Guid userId) =>
            _context.LobbyMembers
                .AsNoTracking()
                .AnyAsync(m => m.LobbyId == lobbyId && m.UserId == userId && m.IsActive);

        public Task<bool> HasRatingAsync(Guid lobbyId, Guid raterUserId, Guid targetUserId) =>
            _context.PlayerKarmaRatings
                .AsNoTracking()
                .AnyAsync(r =>
                    r.LobbyId == lobbyId
                    && r.RaterUserId == raterUserId
                    && r.TargetUserId == targetUserId);

        public async Task<IReadOnlyList<Guid>> GetRatedTargetIdsAsync(Guid lobbyId, Guid raterUserId) =>
            await _context.PlayerKarmaRatings
                .AsNoTracking()
                .Where(r => r.LobbyId == lobbyId && r.RaterUserId == raterUserId)
                .Select(r => r.TargetUserId)
                .ToListAsync();

        public Task AddRatingAsync(PlayerKarmaRating rating)
        {
            _context.PlayerKarmaRatings.Add(rating);
            return Task.CompletedTask;
        }

        public Task AddKarmaLogAsync(KarmaLog log)
        {
            _context.KarmaLogs.Add(log);
            return Task.CompletedTask;
        }

        public Task<UserProfile?> GetProfileForUpdateAsync(Guid userId) =>
            _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
