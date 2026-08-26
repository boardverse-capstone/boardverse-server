using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class GameComponentTemplateRepository : IGameComponentTemplateRepository
    {
        private readonly BoardVerseDbContext _context;

        public GameComponentTemplateRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task<List<GameComponentTemplate>> GetByGameTemplateIdAsync(Guid gameTemplateId, CancellationToken cancellationToken = default) =>
            _context.GameComponentTemplates
                .AsNoTracking()
                .Where(c => c.GameTemplateId == gameTemplateId)
                .OrderBy(c => c.ComponentName)
                .ToListAsync();

        public Task<GameComponentTemplate?> GetByIdAndGameTemplateIdAsync(Guid componentId, Guid gameTemplateId, CancellationToken cancellationToken = default) =>
            _context.GameComponentTemplates
                .FirstOrDefaultAsync(c => c.Id == componentId && c.GameTemplateId == gameTemplateId);

        public Task<bool> IsReferencedByInventoryPenaltyAsync(Guid componentId, CancellationToken cancellationToken = default) =>
            _context.CafeGameComponentPenalties
                .AsNoTracking()
                .AnyAsync(p => p.GameComponentTemplateId == componentId);

        public async Task AddAsync(GameComponentTemplate component, CancellationToken cancellationToken = default)
        {
            await _context.GameComponentTemplates.AddAsync(component);
        }

        public void Remove(GameComponentTemplate component) =>
            _context.GameComponentTemplates.Remove(component);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _context.SaveChangesAsync();
    }
}
