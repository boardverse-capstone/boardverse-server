using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class KarmaShortPlayRecordRepository : IKarmaShortPlayRecordRepository
{
    private readonly BoardVerseDbContext _db;

    public KarmaShortPlayRecordRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<KarmaShortPlayRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.KarmaShortPlayRecords.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<KarmaShortPlayRecord?> GetByReservationAndUserAsync(Guid reservationId, Guid userId, CancellationToken ct = default)
        => _db.KarmaShortPlayRecords
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId && r.UserId == userId, ct);

    // GAP-4: Lookup KarmaShortPlayRecord cho host-dissolve case. Idempotency check dựa trên
    // AppealReason chứa lobbyId — record dissolve của cùng 1 lobby không tạo 2 lần.
    public Task<KarmaShortPlayRecord?> GetLatestDissolveByHostAsync(Guid hostId, Guid lobbyId, CancellationToken ct = default)
        => _db.KarmaShortPlayRecords
            .Where(r => r.UserId == hostId
                && r.ReservationId == null
                && r.AppealReason != null
                && r.AppealReason.Contains(lobbyId.ToString("N")))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<KarmaShortPlayRecord?> GetLatestByUserAsync(Guid userId, CancellationToken ct = default)
        => _db.KarmaShortPlayRecords
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<int> GetActiveCountByUserAsync(Guid userId, CancellationToken ct = default)
        => _db.KarmaShortPlayRecords
            .Where(r => r.UserId == userId && r.Status == KarmaRecordStatus.Active)
            .CountAsync(ct);

    public async Task<int> ExpireOldRecordsAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var oldRecords = await _db.KarmaShortPlayRecords
            .Where(r => r.Status == KarmaRecordStatus.Active && r.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (oldRecords.Count == 0)
        {
            return 0;
        }

        foreach (var r in oldRecords)
        {
            r.Status = KarmaRecordStatus.Expired;
        }

        await _db.SaveChangesAsync(ct);
        return oldRecords.Count;
    }

    public async Task AddAsync(KarmaShortPlayRecord record, CancellationToken ct = default)
    {
        await _db.KarmaShortPlayRecords.AddAsync(record, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(KarmaShortPlayRecord record, CancellationToken ct = default)
    {
        _db.KarmaShortPlayRecords.Update(record);
        await _db.SaveChangesAsync(ct);
    }
}