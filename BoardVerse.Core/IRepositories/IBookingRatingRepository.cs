using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>Repository cho cross-rating sau check-out (mobile gap #5).</summary>
public interface IBookingRatingRepository
{
    Task<BookingRating?> GetByBookingAndVoterAsync(Guid bookingId, Guid voterUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingRating>> GetByBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingRating>> GetUnaggregatedByBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);
    Task AddAsync(BookingRating rating, CancellationToken cancellationToken = default);
    Task UpdateAsync(BookingRating rating, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}