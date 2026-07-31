using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid bookingId, bool includeRelations = false);
    Task<Booking?> GetByLobbyIdAsync(Guid lobbyId);
    Task<IReadOnlyList<Booking>> GetByCafeIdAsync(Guid cafeId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<IReadOnlyList<Booking>> GetByCafeTableIdAsync(Guid cafeTableId);
    Task<IReadOnlyList<Booking>> GetByStatusAsync(BookingStatus status);
    Task<IReadOnlyList<Booking>> GetUpcomingAsync(DateTime cutoff, int limit = 50);
    Task AddAsync(Booking booking);
    Task UpdateAsync(Booking booking);
    Task SaveChangesAsync();
}
