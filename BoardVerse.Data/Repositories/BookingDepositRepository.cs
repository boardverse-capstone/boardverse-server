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
                .Include(d => d.MasterAccount)
                .FirstOrDefaultAsync(d => d.Id == depositId);
        }

        public async Task<BookingDeposit?> GetByOrderIdAsync(string orderId)
        {
            return await _db.BookingDeposits
                .Include(d => d.MasterAccount)
                .FirstOrDefaultAsync(d => d.OrderId == orderId);
        }

        /// <summary>
        /// Host-led check-in: Tìm booking deposit theo mã đặt chỗ (BookingCode = OrderId).
        /// </summary>
        public async Task<BookingDeposit?> GetByBookingCodeAsync(string bookingCode)
        {
            return await _db.BookingDeposits
                .Include(d => d.MasterAccount)
                .FirstOrDefaultAsync(d => d.OrderId == bookingCode);
        }

        public async Task<BookingDeposit?> GetByActiveSessionIdAsync(Guid activeSessionId)
        {
            return await _db.BookingDeposits
                .Include(d => d.MasterAccount)
                .FirstOrDefaultAsync(d => d.ActiveSessionId == activeSessionId);
        }

        public async Task<BookingDeposit?> GetBySePayTransactionIdAsync(string sePayTransactionId)
        {
            return await _db.BookingDeposits
                .Include(d => d.MasterAccount)
                .FirstOrDefaultAsync(d => d.SePayTransactionId == sePayTransactionId);
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

        public async Task<IReadOnlyList<BookingDeposit>> GetPendingExpiredAsync(DateTime cutoffTime)
        {
            return await _db.BookingDeposits
                .Where(d => d.Status == BookingDepositStatus.Pending && d.CreatedAt <= cutoffTime)
                .ToListAsync();
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }
    }
}
