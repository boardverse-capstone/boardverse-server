using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly BoardVerseDbContext _context;

        public CategoryRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task<List<Category>> GetAllActiveAsync() =>
            _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ToListAsync();

        public Task<List<Category>> GetAllAsync(bool includeInactive)
        {
            var query = _context.Categories.AsNoTracking().AsQueryable();
            if (!includeInactive)
                query = query.Where(c => c.IsActive);

            return query.OrderBy(c => c.SortOrder).ToListAsync();
        }

        public Task<Category?> GetByIdAsync(Guid id) =>
            _context.Categories.FirstOrDefaultAsync(c => c.Id == id);

        public Task<Category?> GetBySlugAsync(string slug) =>
            _context.Categories.FirstOrDefaultAsync(c => c.Slug == slug);

        public Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null) =>
            _context.Categories.AnyAsync(c =>
                c.Slug == slug && (!excludeId.HasValue || c.Id != excludeId.Value));

        public Task<int> CountByIdsAsync(IReadOnlyCollection<Guid> ids, bool activeOnly = true)
        {
            if (ids.Count == 0)
                return Task.FromResult(0);

            var query = _context.Categories.AsNoTracking().Where(c => ids.Contains(c.Id));
            if (activeOnly)
                query = query.Where(c => c.IsActive);

            return query.CountAsync();
        }

        public async Task AddAsync(Category category)
        {
            await _context.Categories.AddAsync(category);
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
