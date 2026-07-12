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
        Task AddAsync(BookingDeposit deposit);
        Task UpdateAsync(BookingDeposit deposit);
        Task<IReadOnlyList<BookingDeposit>> GetPendingExpiredAsync(DateTime cutoffTime);
        Task SaveChangesAsync();
    }
}
