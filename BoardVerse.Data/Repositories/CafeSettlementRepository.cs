using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class CafeSettlementRepository : ICafeSettlementRepository
    {
        private readonly BoardVerseDbContext _db;

        public CafeSettlementRepository(BoardVerseDbContext db)
        {
            _db = db;
        }

        public Task AddAsync(CafeSettlement settlement)
        {
            _db.CafeSettlements.Add(settlement);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CafeSettlement settlement)
        {
            settlement.UpdatedAt = DateTime.UtcNow;
            _db.CafeSettlements.Update(settlement);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<CafeSettlement>> GetPendingAsync(Guid cafeId)
        {
            return await _db.CafeSettlements
                .Where(s => s.CafeId == cafeId && s.Status == Core.Enum.CafeSettlementStatus.Pending)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<CafeSettlement>> GetRetryableAsync(int maxAttempts, TimeSpan minRetryDelay)
        {
            var cutoff = DateTime.UtcNow - minRetryDelay;
            return await _db.CafeSettlements
                .Where(s => s.Status == Core.Enum.CafeSettlementStatus.Failed
                    && s.RetryCount < maxAttempts
                    && (s.NextRetryAt == null || s.NextRetryAt <= DateTime.UtcNow)
                    && s.UpdatedAt <= cutoff)
                .OrderBy(s => s.UpdatedAt)
                .ToListAsync();
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }
    }
}
