using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// Repository cho WalkInWindow entity.
/// Implements OCC-based seat reservation for EC-06 (race condition).
/// </summary>
public class WalkInWindowRepository : IWalkInWindowRepository
{
    private readonly BoardVerseDbContext _db;

    public WalkInWindowRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<WalkInWindow?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.WalkInWindows.FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<WalkInWindow?> GetByIdWithBookingsAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.WalkInWindows
            .Include(w => w.WalkInBookings)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<IReadOnlyList<WalkInWindow>> GetActiveByCafeAsync(
        Guid cafeId, DateOnly date, CancellationToken ct = default)
    {
        var startOfDay = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endOfDay = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var now = DateTime.UtcNow;

        // GAP-R6-TW Fix: filter WindowEnd > now (UtcNow) để loại bỏ windows đã expire
        // nhưng background job chưa flip sang Closed/Expired status.
        // Trước đây: WindowEnd > startOfDay (đầu ngày) → trả về cả windows của hôm qua
        // đã hết hạn từ lâu → leak window cũ vào discovery / POS view.
        return await _db.WalkInWindows
            .Where(w => w.CafeId == cafeId
                && w.WindowStart < endOfDay
                && w.WindowEnd > startOfDay
                && w.WindowEnd > now
                && w.Status != WalkInWindowStatus.Closed
                && w.Status != WalkInWindowStatus.Expired)
            .OrderBy(w => w.WindowStart)
            .ToListAsync(ct);
    }

    public async Task<bool> TryHoldSeatsAsync(
        Guid windowId, int seatsToHold, uint expectedVersion, CancellationToken ct = default)
    {
        // Use raw SQL with Version column for PostgreSQL OCC (BR-WALKIN-05).
        // xmin là system column khác hoàn toàn — không map với entity.Version.
        // So sánh trực tiếp "Version" = expectedVersion để phát hiện stale write.
        // Bug fix 2026-08-13: IN clause phải bao gồm Available (0) — trước đó chỉ {Full, Partial}
        // nên WalkInWindow mới tạo (Status = Available) không bao giờ hold được.
        // Đúng: chỉ hold được khi Status ∈ {Available, Partial} (Full đã hết ghế).
        var rowsAffected = await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "WalkInWindows"
            SET "AvailableSeats" = "AvailableSeats" - {0},
                "HeldSeats" = "HeldSeats" + {0},
                "Status" = CASE
                    WHEN "AvailableSeats" - {0} <= 0 THEN {1}
                    ELSE {2}
                END,
                "Version" = "Version" + 1
            WHERE "Id" = {3}
              AND "Version" = {4}
              AND "Status" IN ({5}, {2})
              AND "AvailableSeats" >= {0};
            """,
            seatsToHold,
            (int)WalkInWindowStatus.Full,
            (int)WalkInWindowStatus.Partial,
            windowId,
            (long)expectedVersion,
            (int)WalkInWindowStatus.Available);

        return rowsAffected > 0;
    }

    public async Task<bool> TryReleaseSeatsAsync(
        Guid windowId, int seatsToRelease, uint expectedVersion, CancellationToken ct = default)
    {
        // OCC trên Version column (BR-WALKIN-05) — phát hiện stale write.
        var rowsAffected = await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "WalkInWindows"
            SET "AvailableSeats" = "AvailableSeats" + {0},
                "HeldSeats" = "HeldSeats" - {0},
                "Status" = {1},
                "Version" = "Version" + 1
            WHERE "Id" = {2}
              AND "Version" = {3}
              AND "Status" IN ({4}, {5})
              AND "HeldSeats" >= {0};
            """,
            seatsToRelease,
            (int)WalkInWindowStatus.Available,
            windowId,
            (long)expectedVersion,
            (int)WalkInWindowStatus.Partial,
            (int)WalkInWindowStatus.Full);

        return rowsAffected > 0;
    }

    public async Task<WalkInWindow> AddAsync(WalkInWindow window, CancellationToken ct = default)
    {
        _db.WalkInWindows.Add(window);
        await _db.SaveChangesAsync(ct);
        return window;
    }

    public async Task CloseAsync(Guid windowId, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync(
            """UPDATE "WalkInWindows" SET "Status" = {0} WHERE "Id" = {1};""",
            (int)WalkInWindowStatus.Closed, windowId);
    }

    public async Task<IReadOnlyList<WalkInWindow>> GetExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.WalkInWindows
            .Where(w => w.WindowEnd < now
                && w.Status != WalkInWindowStatus.Closed
                && w.Status != WalkInWindowStatus.Expired)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WalkInWindow>> GetOverlappingAsync(
        Guid cafeId, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default)
    {
        // Overlap: existing window intersects with proposed [windowStart, windowEnd)
        return await _db.WalkInWindows
            .Where(w => w.CafeId == cafeId
                && w.Status != WalkInWindowStatus.Closed
                && w.Status != WalkInWindowStatus.Expired
                && w.WindowStart < windowEnd
                && w.WindowEnd > windowStart)
            .ToListAsync(ct);
    }

    /// <summary>
    /// GAP-14 Fix: Lấy WalkInWindow active (Available/Partial) cho 1 Reservation.
    /// </summary>
    public async Task<WalkInWindow?> GetActiveByReservationIdAsync(Guid reservationId, CancellationToken ct = default)
    {
        return await _db.WalkInWindows
            .Where(w => w.SourceReservationId == reservationId
                && (w.Status == WalkInWindowStatus.Available
                    || w.Status == WalkInWindowStatus.Partial))
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }
}
