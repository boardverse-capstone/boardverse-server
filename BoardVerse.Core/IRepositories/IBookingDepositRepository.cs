using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

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

        // === GAP-C4: Atomic state transitions for SePay webhook idempotency ===
        // Updates only succeed when current status matches expectedStatus; returns rows affected.
        // Returns 0 if no row was updated (already in target state, or stale status) — caller treats as duplicate.
        Task<int> TryMarkAsPaidAsync(Guid depositId, string? sePayTransactionId, DateTime paidAtUtc);
        Task<int> TryMarkAsRefundedAsync(Guid depositId, DateTime refundedAtUtc);
        Task<int> TryForfeitAsync(Guid depositId, DateTime forfeitedAtUtc);
        Task<int> TryExpireAsync(Guid depositId, DateTime refundedAtUtc);

        // === Admin: Reports ===
        /// <summary>
        /// Đếm số deposit theo status (BookingDepositStatus enum).
        /// </summary>
        Task<int> CountByStatusAsync(BookingDepositStatus status, DateTime? fromUtc, DateTime? toUtc);
        /// <summary>
        /// Tổng số deposit + tổng amount theo status trong khoảng thời gian.
        /// </summary>
        Task<(int Count, decimal TotalAmount)> SumByStatusAsync(BookingDepositStatus status, DateTime? fromUtc, DateTime? toUtc);
    }
}
