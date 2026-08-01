using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>Repository cho cross-rating sau check-out (mobile gap #5).</summary>
public interface IBookingRatingRepository
{
    Task<BookingRating?> GetByBookingAndVoterAsync(Guid bookingId, Guid voterUserId);
    Task<IReadOnlyList<BookingRating>> GetByBookingAsync(Guid bookingId);
    Task<IReadOnlyList<BookingRating>> GetUnaggregatedByBookingAsync(Guid bookingId);
    Task AddAsync(BookingRating rating);
    Task UpdateAsync(BookingRating rating);
    Task SaveChangesAsync();
}