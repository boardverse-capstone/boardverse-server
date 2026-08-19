using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface ICafeShiftRepository
{
    Task AddAsync(CafeShift shift);
    Task UpdateAsync(CafeShift shift);
    Task<CafeShift?> GetByIdAsync(Guid id);
    Task<CafeShift?> GetCurrentOpenShiftAsync(Guid cafeId);
    Task<IReadOnlyList<CafeShift>> GetHistoryAsync(Guid cafeId, int page, int pageSize);
    Task<int> GetHistoryCountAsync(Guid cafeId);
    Task SaveChangesAsync();
}
