using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid bookingId, bool includeRelations = false);

    /// <summary>
    /// Lấy booking theo lobby ID (includeRelations=true kèm Cafe, CafeTable, Lobby.GameTemplate,
    /// Lobby.Members, BookingDeposit). Mặc định includeRelations=true để phục vụ mobile UI.
    /// </summary>
    Task<Booking?> GetByLobbyIdAsync(Guid lobbyId, bool includeRelations = true);
    Task<IReadOnlyList<Booking>> GetByCafeIdAsync(Guid cafeId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<IReadOnlyList<Booking>> GetByCafeTableIdAsync(Guid cafeTableId);
    Task<IReadOnlyList<Booking>> GetConflictingBookingsWithLockAsync(Guid cafeTableId, DateTime startTime, DateTime endTime);
    /// <summary>
    /// Lấy các booking của 1 cafe có overlap với khung giờ [start, end] và KHÔNG Cancelled.
    /// Dùng cho API availability (#2) và available-tables (#1) — không cần lock.
    /// </summary>
    Task<IReadOnlyList<Booking>> GetOverlappingBookingsAsync(Guid cafeId, DateTime startTime, DateTime endTime);
    Task<IReadOnlyList<Booking>> GetByStatusAsync(BookingStatus status);
    Task<IReadOnlyList<Booking>> GetUpcomingAsync(DateTime cutoff, int limit = 50);
    Task AddAsync(Booking booking);
    Task UpdateAsync(Booking booking);
    Task SaveChangesAsync();

    // === Admin: Reports ===
    /// <summary>
    /// Đếm bookings theo trạng thái trong khoảng thời gian.
    /// </summary>
    Task<int> CountByStatusAsync(BookingStatus status, DateTime? fromUtc, DateTime? toUtc);
    /// <summary>
    /// Đếm tổng bookings.
    /// </summary>
    Task<int> CountAllAsync(DateTime? fromUtc, DateTime? toUtc);

}
