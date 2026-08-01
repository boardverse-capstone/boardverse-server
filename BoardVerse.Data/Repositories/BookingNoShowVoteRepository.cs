using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class BookingNoShowVoteRepository : IBookingNoShowVoteRepository
{
    private readonly BoardVerseDbContext _db;

    public BookingNoShowVoteRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<BookingNoShowVote?> GetByBookingAndVoterAsync(Guid bookingId, Guid voterUserId)
    {
        return await _db.BookingNoShowVotes
            .FirstOrDefaultAsync(v => v.BookingId == bookingId && v.VoterUserId == voterUserId);
    }

    public async Task<IReadOnlyList<BookingNoShowVote>> GetByBookingAsync(Guid bookingId)
    {
        return await _db.BookingNoShowVotes
            .Where(v => v.BookingId == bookingId)
            .ToListAsync();
    }

    public async Task AddAsync(BookingNoShowVote vote)
    {
        await _db.BookingNoShowVotes.AddAsync(vote);
    }

    public Task UpdateAsync(BookingNoShowVote vote)
    {
        _db.BookingNoShowVotes.Update(vote);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}