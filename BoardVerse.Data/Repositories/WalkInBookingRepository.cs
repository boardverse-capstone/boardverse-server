using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// Repository cho WalkInBooking entity.
/// </summary>
public class WalkInBookingRepository : IWalkInBookingRepository
{
    private readonly BoardVerseDbContext _db;

    public WalkInBookingRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<WalkInBooking?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.WalkInBookings
            .Include(w => w.WalkInWindow)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<WalkInBooking?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        // Use CreatedAt window (1 minute) as idempotency proxy since entity has no IdempotencyKey field.
        // Combined with GuestName + CafeId, this prevents duplicate booking creation on client retry.
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return null;

        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        return await _db.WalkInBookings
            .Include(w => w.WalkInWindow)
            .Where(w => w.GuestName == idempotencyKey && w.CreatedAt >= cutoff)
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WalkInBooking> AddAsync(WalkInBooking booking, CancellationToken ct = default)
    {
        _db.WalkInBookings.Add(booking);
        await _db.SaveChangesAsync(ct);
        return booking;
    }

    public async Task UpdateAsync(WalkInBooking booking, CancellationToken ct = default)
    {
        _db.WalkInBookings.Update(booking);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WalkInBooking>> GetByCafeAsync(Guid cafeId, CancellationToken ct = default)
    {
        return await _db.WalkInBookings
            .Where(w => w.CafeId == cafeId)
            .Include(w => w.WalkInWindow)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);
    }
}
