using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho <see cref="BookingNoShowVote"/> — lưu phiếu vote vắng mặt của từng member booking.
/// </summary>
public interface IBookingNoShowVoteRepository
{
    Task<BookingNoShowVote?> GetByBookingAndVoterAsync(Guid bookingId, Guid voterUserId);
    Task<IReadOnlyList<BookingNoShowVote>> GetByBookingAsync(Guid bookingId);
    Task AddAsync(BookingNoShowVote vote);
    Task UpdateAsync(BookingNoShowVote vote);
    Task SaveChangesAsync();
}