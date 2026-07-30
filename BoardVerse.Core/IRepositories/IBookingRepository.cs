using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid bookingId, bool includeRelations = false);
    Task<Booking?> GetByIdWithDepositAsync(Guid bookingId);
    Task<Booking?> GetByLobbyIdAsync(Guid lobbyId);
    Task<IReadOnlyList<Booking>> GetByUserIdAsync(Guid userId, BookingStatus? status = null);
    Task<IReadOnlyList<Booking>> GetByCafeIdAsync(Guid cafeId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<IReadOnlyList<Booking>> GetUpcomingByUserIdAsync(Guid userId, int limit = 10);
    Task AddAsync(Booking booking);
    Task UpdateAsync(Booking booking);
    Task SaveChangesAsync();
}
