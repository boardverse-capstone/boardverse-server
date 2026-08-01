using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class BookingRatingRepository : IBookingRatingRepository
{
    private readonly BoardVerseDbContext _db;

    public BookingRatingRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<BookingRating?> GetByBookingAndVoterAsync(Guid bookingId, Guid voterUserId)
    {
        return await _db.BookingRatings
            .FirstOrDefaultAsync(r => r.BookingId == bookingId && r.VoterUserId == voterUserId);
    }

    public async Task<IReadOnlyList<BookingRating>> GetByBookingAsync(Guid bookingId)
    {
        return await _db.BookingRatings
            .Where(r => r.BookingId == bookingId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<BookingRating>> GetUnaggregatedByBookingAsync(Guid bookingId)
    {
        return await _db.BookingRatings
            .Where(r => r.BookingId == bookingId && !r.IsAggregated)
            .ToListAsync();
    }

    public async Task AddAsync(BookingRating rating)
    {
        await _db.BookingRatings.AddAsync(rating);
    }

    public Task UpdateAsync(BookingRating rating)
    {
        _db.BookingRatings.Update(rating);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}