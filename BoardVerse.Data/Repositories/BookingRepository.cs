using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly BoardVerseDbContext _db;

    public BookingRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<Booking?> GetByIdAsync(Guid bookingId, bool includeRelations = false)
    {
        var query = _db.Bookings.AsQueryable();

        if (includeRelations)
        {
            query = query
                .Include(b => b.Cafe)
                .Include(b => b.CafeTable)
                .Include(b => b.Lobby)
                    .ThenInclude(l => l!.GameTemplate)
                .Include(b => b.Lobby)
                    .ThenInclude(l => l!.Members)
                .Include(b => b.BookingDeposit);
        }

        return await query.FirstOrDefaultAsync(b => b.Id == bookingId);
    }

    public async Task<Booking?> GetByLobbyIdAsync(Guid lobbyId, bool includeRelations = true)
    {
        var query = _db.Bookings.AsQueryable();

        if (includeRelations)
        {
            query = query
                .Include(b => b.Cafe)
                .Include(b => b.CafeTable)
                .Include(b => b.Lobby)
                    .ThenInclude(l => l!.GameTemplate)
                .Include(b => b.Lobby)
                    .ThenInclude(l => l!.Members)
                .Include(b => b.BookingDeposit);
        }

        return await query.FirstOrDefaultAsync(b => b.LobbyId == lobbyId);
    }

    public async Task<IReadOnlyList<Booking>> GetByCafeIdAsync(
        Guid cafeId,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = _db.Bookings.Where(b => b.CafeId == cafeId);

        if (fromDate.HasValue)
            query = query.Where(b => b.ScheduledStartTime >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(b => b.ScheduledStartTime <= toDate.Value);

        return await query
            .Include(b => b.CafeTable)
            .Include(b => b.Lobby)
            .OrderBy(b => b.ScheduledStartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Booking>> GetByCafeTableIdAsync(Guid cafeTableId)
    {
        return await _db.Bookings
            .Where(b => b.CafeTableId == cafeTableId)
            .OrderBy(b => b.ScheduledStartTime)
            .ToListAsync();
    }

    /// <summary>
    /// Lấy các booking của 1 cafe overlap [start, end] và không Cancelled.
    /// Dùng cho mobile availability/available-tables (read-only, không lock).
    /// </summary>
    public async Task<IReadOnlyList<Booking>> GetOverlappingBookingsAsync(
        Guid cafeId, DateTime startTime, DateTime endTime)
    {
        return await _db.Bookings
            .Where(b => b.CafeId == cafeId
                && b.Status != BookingStatus.Cancelled
                && b.ScheduledStartTime < endTime
                && b.ScheduleEndTime > startTime)
            .ToListAsync();
    }

    /// <summary>
    /// P1 Fix #6: Get conflicting bookings with pessimistic lock for race condition prevention.
    /// Uses raw SQL with FOR UPDATE to lock rows during transaction.
    /// </summary>
    public async Task<IReadOnlyList<Booking>> GetConflictingBookingsWithLockAsync(
        Guid cafeTableId, DateTime startTime, DateTime endTime)
    {
        // Use raw SQL with FOR UPDATE SKIP LOCKED for pessimistic locking
        // This prevents race conditions when multiple bookings are created simultaneously
        // Note: Only Cancelled is a terminal state - Confirmed/CheckedIn are not
        var conflictingBookings = await _db.Bookings
            .FromSqlRaw(
                @"SELECT * FROM ""Bookings""
                  WHERE ""CafeTableId"" = {0}
                  AND ""Status"" != {1}
                  AND ""ScheduledStartTime"" < {2}
                  AND ""ScheduleEndTime"" > {3}
                  FOR UPDATE SKIP LOCKED",
                cafeTableId,
                (int)BookingStatus.Cancelled,
                endTime,
                startTime)
            .ToListAsync();

        return conflictingBookings;
    }

    public async Task<IReadOnlyList<Booking>> GetByStatusAsync(BookingStatus status)
    {
        return await _db.Bookings
            .Where(b => b.Status == status)
            .OrderBy(b => b.ScheduledStartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Booking>> GetUpcomingAsync(DateTime cutoff, int limit = 50)
    {
        return await _db.Bookings
            .Where(b => b.ScheduledStartTime >= DateTime.UtcNow &&
                        b.ScheduledStartTime <= cutoff)
            .Where(b => b.Status == BookingStatus.PendingDeposit ||
                        b.Status == BookingStatus.Confirmed)
            .OrderBy(b => b.ScheduledStartTime)
            .Take(limit)
            .ToListAsync();
    }

    public async Task AddAsync(Booking booking)
    {
        await _db.Bookings.AddAsync(booking);
    }

    public async Task UpdateAsync(Booking booking)
    {
        _db.Bookings.Update(booking);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
