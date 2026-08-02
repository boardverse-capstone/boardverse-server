using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IBookingDepositRepository
    {
        Task<BookingDeposit?> GetByIdAsync(Guid depositId);
        Task<BookingDeposit?> GetByOrderIdAsync(string orderId);
        Task<BookingDeposit?> GetByBookingCodeAsync(string bookingCode);
        Task<BookingDeposit?> GetByActiveSessionIdAsync(Guid activeSessionId);
        Task<BookingDeposit?> GetBySePayTransactionIdAsync(string sePayTransactionId);
        /// <summary>BR-05: Lấy deposit theo BookingId.</summary>
        Task<BookingDeposit?> GetByBookingIdAsync(Guid bookingId);
        Task AddAsync(BookingDeposit deposit);
        Task UpdateAsync(BookingDeposit deposit);
        Task<IReadOnlyList<BookingDeposit>> GetPendingExpiredAsync(DateTime cutoffTime, int limit = 100);
        Task SaveChangesAsync();
    }
}
