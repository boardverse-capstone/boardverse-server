using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface ICafeTableRepository
{
    Task<CafeTable?> GetByIdAsync(Guid tableId);
    Task<IReadOnlyList<CafeTable>> GetByCafeIdAsync(Guid cafeId);
    Task UpdateAsync(CafeTable table);
    Task SaveChangesAsync();
}
