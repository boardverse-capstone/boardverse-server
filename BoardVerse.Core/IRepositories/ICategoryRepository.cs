using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ICategoryRepository
    {
        Task<List<Category>> GetAllActiveAsync(CancellationToken cancellationToken = default);
        Task<List<Category>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
        Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
        Task<int> CountByIdsAsync(IReadOnlyCollection<Guid> ids, bool activeOnly = true, CancellationToken cancellationToken = default);
        Task AddAsync(Category category, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
