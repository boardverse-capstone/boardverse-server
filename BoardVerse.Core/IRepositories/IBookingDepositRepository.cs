using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IBookingDepositRepository
    {
        Task<BookingDeposit?> GetByIdAsync(Guid depositId, CancellationToken cancellationToken = default);
        Task<BookingDeposit?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
        Task<BookingDeposit?> GetByBookingCodeAsync(string bookingCode, CancellationToken cancellationToken = default);
        Task<BookingDeposit?> GetByActiveSessionIdAsync(Guid activeSessionId, CancellationToken cancellationToken = default);
        Task<BookingDeposit?> GetBySePayTransactionIdAsync(string sePayTransactionId, CancellationToken cancellationToken = default);
        /// <summary>BR-05: Lấy deposit theo BookingId.</summary>
        Task<BookingDeposit?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken = default);
        Task AddAsync(BookingDeposit deposit, CancellationToken cancellationToken = default);
        Task UpdateAsync(BookingDeposit deposit, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BookingDeposit>> GetPendingExpiredAsync(DateTime cutoffTime, int limit = 100, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        // === GAP-C4: Atomic state transitions for SePay webhook idempotency ===
        // Updates only succeed when current status matches expectedStatus; returns rows affected.
        // Returns 0 if no row was updated (already in target state, or stale status) — caller treats as duplicate.
        Task<int> TryMarkAsPaidAsync(Guid depositId, string? sePayTransactionId, DateTime paidAtUtc, CancellationToken cancellationToken = default);
        Task<int> TryMarkAsRefundedAsync(Guid depositId, DateTime refundedAtUtc, CancellationToken cancellationToken = default);
        Task<int> TryForfeitAsync(Guid depositId, DateTime forfeitedAtUtc, CancellationToken cancellationToken = default);
        Task<int> TryExpireAsync(Guid depositId, DateTime refundedAtUtc, CancellationToken cancellationToken = default);

        // === Admin: Reports ===
        /// <summary>
        /// Đếm số deposit theo status (BookingDepositStatus enum).
        /// </summary>
        Task<int> CountByStatusAsync(BookingDepositStatus status, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);
        /// <summary>
        /// Tổng số deposit + tổng amount theo status trong khoảng thời gian.
        /// </summary>
        Task<(int Count, decimal TotalAmount)> SumByStatusAsync(BookingDepositStatus status, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);
    }
}
