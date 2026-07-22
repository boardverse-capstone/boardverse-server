using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class LobbyMessageRepository : ILobbyMessageRepository
    {
        private readonly BoardVerseDbContext _db;

        public LobbyMessageRepository(BoardVerseDbContext db)
        {
            _db = db;
        }

        public async Task<LobbyMessage?> GetByIdAsync(Guid id)
        {
            return await _db.LobbyMessages
                .Include(m => m.Sender)
                    .ThenInclude(u => u.Profile)
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<IReadOnlyList<LobbyMessage>> GetByLobbyAsync(Guid lobbyId, DateTime? beforeCursor, int limit = 50)
        {
            var query = _db.LobbyMessages
                .Include(m => m.Sender)
                    .ThenInclude(u => u.Profile)
                .Where(m => m.LobbyId == lobbyId);

            if (beforeCursor.HasValue)
            {
                query = query.Where(m => m.CreatedAt < beforeCursor.Value);
            }

            var msgs = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return msgs.OrderBy(m => m.CreatedAt).ToList();
        }

        public Task AddAsync(LobbyMessage message)
        {
            _db.LobbyMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }
    }
}