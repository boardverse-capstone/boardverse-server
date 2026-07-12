using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafeSettlementRepository
    {
        Task AddAsync(CafeSettlement settlement);
        Task UpdateAsync(CafeSettlement settlement);
        Task<IReadOnlyList<CafeSettlement>> GetPendingAsync(Guid cafeId);
        Task SaveChangesAsync();
    }
}
