using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface ICafeTableRepository
{
    Task<CafeTable?> GetByIdAsync(Guid tableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CafeTable>> GetByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default);
    Task UpdateAsync(CafeTable table, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
