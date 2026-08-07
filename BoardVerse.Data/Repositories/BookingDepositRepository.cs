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
    }
}
