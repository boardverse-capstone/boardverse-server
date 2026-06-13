using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICategoryRepository
    {
        Task<List<Category>> GetAllActiveAsync();
    }
}
