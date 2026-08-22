using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho <see cref="BookingNoShowVote"/> — lưu phiếu vote vắng mặt của từng member booking.
/// </summary>
public interface IBookingNoShowVoteRepository
{
    Task<BookingNoShowVote?> GetByBookingAndVoterAsync(Guid bookingId, Guid voterUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookingNoShowVote>> GetByBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);
    Task AddAsync(BookingNoShowVote vote, CancellationToken cancellationToken = default);
    Task UpdateAsync(BookingNoShowVote vote, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}