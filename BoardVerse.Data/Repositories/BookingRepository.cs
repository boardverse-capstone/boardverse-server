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
                .Include(b => b.User)
                .Include(b => b.BookingDeposit)
                .Include(b => b.Lobby);
        }

        return await query.FirstOrDefaultAsync(b => b.Id == bookingId);
    }

    public async Task<Booking?> GetByIdWithDepositAsync(Guid bookingId)
    {
        return await _db.Bookings
            .Include(b => b.BookingDeposit)
            .Include(b => b.Cafe)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId);
    }

    public async Task<Booking?> GetByLobbyIdAsync(Guid lobbyId)
    {
        return await _db.Bookings
            .Include(b => b.BookingDeposit)
            .FirstOrDefaultAsync(b => b.LobbyId == lobbyId);
    }

    public async Task<IReadOnlyList<Booking>> GetByUserIdAsync(Guid userId, BookingStatus? status = null)
    {
        var query = _db.Bookings
            .Include(b => b.Cafe)
            .Where(b => b.UserId == userId);

        if (status.HasValue)
            query = query.Where(b => b.Status == status.Value);

        return await query
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Booking>> GetByCafeIdAsync(
        Guid cafeId,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = _db.Bookings.Where(b => b.CafeId == cafeId);

        if (fromDate.HasValue)
            query = query.Where(b => b.BookingDate >= fromDate.Value.Date);

        if (toDate.HasValue)
            query = query.Where(b => b.BookingDate <= toDate.Value.Date);

        return await query
            .Include(b => b.User)
            .Include(b => b.BookingDeposit)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Booking>> GetUpcomingByUserIdAsync(Guid userId, int limit = 10)
    {
        var today = DateTime.UtcNow.Date;
        return await _db.Bookings
            .Include(b => b.Cafe)
            .Include(b => b.BookingDeposit)
            .Where(b => b.UserId == userId)
            .Where(b => b.BookingDate >= today)
            .Where(b => b.Status == BookingStatus.Confirmed ||
                        b.Status == BookingStatus.PendingDeposit ||
                        b.Status == BookingStatus.PendingPayment)
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .Take(limit)
            .ToListAsync();
    }

    public async Task AddAsync(Booking booking)
    {
        await _db.Bookings.AddAsync(booking);
    }

    public async Task UpdateAsync(Booking booking)
    {
        booking.UpdatedAt = DateTime.UtcNow;
        _db.Bookings.Update(booking);
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
