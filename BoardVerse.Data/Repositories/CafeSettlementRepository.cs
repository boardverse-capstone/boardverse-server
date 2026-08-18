using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
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

        /// <summary>W-06: Get settlement by Id for admin override.</summary>
        public async Task<CafeSettlement?> GetByIdAsync(Guid settlementId)
        {
            return await _db.CafeSettlements.FirstOrDefaultAsync(s => s.Id == settlementId);
        }

        /// <summary>
        /// W-06: Admin list settlements với filter + phân trang, kèm CafeName join để admin dễ nhận diện.
        /// Sort mặc định theo UpdatedAt DESC để Failed mới nhất nằm trên (partial index tối ưu).
        /// </summary>
        public async Task<PaginatedResponse<SettlementListItemDto>> GetPagedAsync(SettlementListQuery q)
        {
            var query = _db.CafeSettlements.AsNoTracking().AsQueryable();

            if (q.Status.HasValue) query = query.Where(s => s.Status == q.Status.Value);
            if (q.CafeId.HasValue) query = query.Where(s => s.CafeId == q.CafeId.Value);
            if (q.CafeManagerId.HasValue) query = query.Where(s => s.CafeManagerId == q.CafeManagerId.Value);
            if (q.FromUtc.HasValue) query = query.Where(s => s.CreatedAt >= q.FromUtc.Value);
            if (q.ToUtc.HasValue) query = query.Where(s => s.CreatedAt <= q.ToUtc.Value);

            var total = await query.CountAsync();

            var items = await (
                from s in query
                join c in _db.Cafes.AsNoTracking() on s.CafeId equals c.Id into cafeJoin
                from c in cafeJoin.DefaultIfEmpty()
                orderby s.UpdatedAt descending
                select new SettlementListItemDto
                {
                    Id = s.Id,
                    CafeId = s.CafeId,
                    CafeName = c != null ? c.Name : null,
                    CafeManagerId = s.CafeManagerId,
                    ActiveSessionId = s.ActiveSessionId,
                    BookingDepositId = s.BookingDepositId,
                    DepositAmount = s.DepositAmount,
                    FeeAmount = s.FeeAmount,
                    NetTransferAmount = s.NetTransferAmount,
                    SePayTransferId = s.SePayTransferId,
                    Status = s.Status,
                    FailureReason = s.FailureReason,
                    RetryCount = s.RetryCount,
                    NextRetryAt = s.NextRetryAt,
                    TransferredAt = s.TransferredAt,
                    OverrideBy = s.OverrideBy,
                    OverrideAt = s.OverrideAt,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt
                })
                .Skip((q.PageNumber - 1) * q.PageSize)
                .Take(q.PageSize)
                .ToListAsync();

            return new PaginatedResponse<SettlementListItemDto>
            {
                Data = items,
                Meta = new PaginationMeta
                {
                    CurrentPage = q.PageNumber,
                    PageSize = q.PageSize,
                    TotalItems = total,
                    TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)q.PageSize)
                }
            };
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }
    }
}
