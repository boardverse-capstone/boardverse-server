using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICategoryRepository
    {
        Task<List<Category>> GetAllActiveAsync();
        Task<List<Category>> GetAllAsync(bool includeInactive);
        Task<Category?> GetByIdAsync(Guid id);
        Task<Category?> GetBySlugAsync(string slug);
        Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null);
        Task<int> CountByIdsAsync(IReadOnlyCollection<Guid> ids, bool activeOnly = true);
        Task AddAsync(Category category);
        Task SaveChangesAsync();
    }
}
