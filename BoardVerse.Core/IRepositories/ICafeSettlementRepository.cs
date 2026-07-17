using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafeSettlementRepository
    {
        Task AddAsync(CafeSettlement settlement);
        Task UpdateAsync(CafeSettlement settlement);
        Task<IReadOnlyList<CafeSettlement>> GetPendingAsync(Guid cafeId);

        /// <summary>Get all settlements with Status=Failed (for retry job).</summary>
        Task<IReadOnlyList<CafeSettlement>> GetRetryableAsync(int maxAttempts, TimeSpan minRetryDelay);
        Task SaveChangesAsync();
    }
}