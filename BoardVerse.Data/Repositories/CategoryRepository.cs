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
    }
}
