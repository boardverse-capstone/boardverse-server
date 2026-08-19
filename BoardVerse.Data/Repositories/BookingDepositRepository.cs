using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class BookingDepositRepository : IBookingDepositRepository
    {
        private readonly BoardVerseDbContext _db;

        public BookingDepositRepository(BoardVerseDbContext db)
        {
            _db = db;
        }

        public async Task<BookingDeposit?> GetByIdAsync(Guid depositId)
        {
            return await _db.BookingDeposits
                .Include(d => d.Cafe)
                .FirstOrDefaultAsync(d => d.Id == depositId);
        }

        public async Task<BookingDeposit?> GetByOrderIdAsync(string orderId)
        {
            return await _db.BookingDeposits
                .Include(d => d.Cafe)
                .FirstOrDefaultAsync(d => d.OrderId == orderId);
        }

        /// <summary>
        /// Host-led check-in: Tìm booking deposit theo mã đặt chỗ (BookingCode = OrderId).
        /// </summary>
        public async Task<BookingDeposit?> GetByBookingCodeAsync(string bookingCode)
        {
            return await _db.BookingDeposits
                .Include(d => d.Cafe)
                .FirstOrDefaultAsync(d => d.OrderId == bookingCode);
        }

        public async Task<BookingDeposit?> GetByActiveSessionIdAsync(Guid activeSessionId)
        {
            return await _db.BookingDeposits
                .FirstOrDefaultAsync(d => d.ActiveSessionId == activeSessionId);
        }

        public async Task<BookingDeposit?> GetBySePayTransactionIdAsync(string sePayTransactionId)
        {
            return await _db.BookingDeposits
                .FirstOrDefaultAsync(d => d.SePayTransactionId == sePayTransactionId);
        }

        /// <summary>BR-05: Lấy deposit theo BookingId.</summary>
        public async Task<BookingDeposit?> GetByBookingIdAsync(Guid bookingId)
        {
            return await _db.BookingDeposits
                .FirstOrDefaultAsync(d => d.BookingId == bookingId);
        }

        public Task AddAsync(BookingDeposit deposit)
        {
            _db.BookingDeposits.Add(deposit);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(BookingDeposit deposit)
        {
            deposit.UpdatedAt = DateTime.UtcNow;
            _db.BookingDeposits.Update(deposit);
            return Task.CompletedTask;
        }

        /// <summary>
        /// GAP #26 fix: cluster-safe + push filter xuống SQL + limit batch.
        /// Dùng FOR UPDATE SKIP LOCKED — multi-instance không pick trùng.
        /// Caller phải wrap batch transaction.
        /// </summary>
        public async Task<IReadOnlyList<BookingDeposit>> GetPendingExpiredAsync(DateTime cutoffTime, int limit = 100)
        {
            return await _db.BookingDeposits
                .FromSqlRaw(
                    "SELECT * FROM \"BookingDeposits\" " +
                    "WHERE \"Status\" = {0} AND \"CreatedAt\" <= {1} " +
                    "ORDER BY \"CreatedAt\" " +
                    "LIMIT {2} " +
                    "FOR UPDATE SKIP LOCKED",
                    (int)BookingDepositStatus.Pending, cutoffTime, limit)
                .ToListAsync();
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }

        // === GAP-C4: Atomic state transitions (idempotent under SePay duplicate webhooks) ===
        // ExecuteUpdateAsync translates to a single SQL UPDATE with WHERE clause including current status.
        // Concurrent webhooks race only at the DB row level — last writer wins only if both attempt
        // the same source status; PG row lock guarantees serialization. RowsAffected=0 means the
        // caller is the loser (or duplicate) → return without mutation.

        public async Task<int> TryMarkAsPaidAsync(Guid depositId, string? sePayTransactionId, DateTime paidAtUtc)
        {
            return await _db.BookingDeposits
                .Where(d => d.Id == depositId && d.Status == BookingDepositStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, BookingDepositStatus.Paid)
                    .SetProperty(d => d.PaidAt, paidAtUtc)
                    .SetProperty(d => d.SePayTransactionId, d => sePayTransactionId ?? d.SePayTransactionId)
                    .SetProperty(d => d.UpdatedAt, paidAtUtc));
        }

        public async Task<int> TryMarkAsRefundedAsync(Guid depositId, DateTime refundedAtUtc)
        {
            return await _db.BookingDeposits
                .Where(d => d.Id == depositId && d.Status == BookingDepositStatus.Paid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, BookingDepositStatus.Refunded)
                    .SetProperty(d => d.RefundedAt, refundedAtUtc)
                    .SetProperty(d => d.UpdatedAt, refundedAtUtc));
        }

        public async Task<int> TryForfeitAsync(Guid depositId, DateTime forfeitedAtUtc)
        {
            return await _db.BookingDeposits
                .Where(d => d.Id == depositId && d.Status == BookingDepositStatus.Paid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, BookingDepositStatus.Forfeited)
                    .SetProperty(d => d.ForfeitedAt, forfeitedAtUtc)
                    .SetProperty(d => d.UpdatedAt, forfeitedAtUtc));
        }

        public async Task<int> TryExpireAsync(Guid depositId, DateTime refundedAtUtc)
        {
            return await _db.BookingDeposits
                .Where(d => d.Id == depositId && d.Status == BookingDepositStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, BookingDepositStatus.Refunded)
                    .SetProperty(d => d.RefundedAt, refundedAtUtc)
                    .SetProperty(d => d.UpdatedAt, refundedAtUtc));
        }

        public async Task<int> CountByStatusAsync(BookingDepositStatus status, DateTime? fromUtc, DateTime? toUtc)
        {
            var query = _db.BookingDeposits.Where(d => d.Status == status);
            if (fromUtc.HasValue)
            {
                query = query.Where(d => d.CreatedAt >= fromUtc.Value);
            }
            if (toUtc.HasValue)
            {
                query = query.Where(d => d.CreatedAt <= toUtc.Value);
            }
            return await query.CountAsync();
        }

        public async Task<(int Count, decimal TotalAmount)> SumByStatusAsync(BookingDepositStatus status, DateTime? fromUtc, DateTime? toUtc)
        {
            var query = _db.BookingDeposits.Where(d => d.Status == status);
            if (fromUtc.HasValue)
            {
                query = query.Where(d => d.CreatedAt >= fromUtc.Value);
            }
            if (toUtc.HasValue)
            {
                query = query.Where(d => d.CreatedAt <= toUtc.Value);
            }
            var rows = await query
                .GroupBy(d => 1)
                .Select(g => new { Count = g.Count(), Total = g.Sum(d => d.Amount) })
                .FirstOrDefaultAsync();
            return rows == null ? (0, 0m) : (rows.Count, rows.Total);
        }
    }
}
