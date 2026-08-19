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
        // WalkInBooking không có IdempotencyKey field, nhưng ta dùng pattern
        // bằng cách query theo CreatedAt + CafeId + GuestName trong khoảng 1 phút
        // Hoặc có thể mở rộng entity sau này
        // Tạm thời return null để service xử lý
        return null;
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
