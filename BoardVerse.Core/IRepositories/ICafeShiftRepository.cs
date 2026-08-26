using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface ICafeShiftRepository
{
    Task AddAsync(CafeShift shift, CancellationToken cancellationToken = default);
    Task UpdateAsync(CafeShift shift, CancellationToken cancellationToken = default);
    Task<CafeShift?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CafeShift?> GetCurrentOpenShiftAsync(Guid cafeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CafeShift>> GetHistoryAsync(Guid cafeId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> GetHistoryCountAsync(Guid cafeId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
