using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class BvcLedgerEntryRepository : IBvcLedgerEntryRepository
{
    private readonly BoardVerseDbContext _db;

    public BvcLedgerEntryRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<BvcLedgerEntry?> GetByIdempotencyKeyAsync(string idempotencyKey)
    {
        return _db.BvcLedgerEntries
            .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);
    }

    /// <summary>
    /// GAP #13 fix: FOR UPDATE lock row ledger theo idempotencyKey.
    /// Phải gọi trong transaction (Serializable Isolation).
    /// </summary>
    public Task<BvcLedgerEntry?> GetByIdempotencyKeyForUpdateAsync(string idempotencyKey)
    {
        return _db.BvcLedgerEntries
            .FromSqlInterpolated($"SELECT * FROM \"BvcLedgerEntries\" WHERE \"IdempotencyKey\" = {idempotencyKey} FOR UPDATE")
            .FirstOrDefaultAsync();
    }

    public Task<BvcLedgerEntry?> GetByIdAsync(Guid id)
    {
        return _db.BvcLedgerEntries.FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<IReadOnlyList<BvcLedgerEntry>> GetHistoryAsync(Guid userId, int page, int pageSize)
    {
        return await _db.BvcLedgerEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public Task<int> CountByUserAsync(Guid userId)
    {
        return _db.BvcLedgerEntries.CountAsync(e => e.UserId == userId);
    }

    public async Task<decimal> SumForfeitAsync(Guid userId, DateTime since)
    {
        // Dùng decimal để tổng quát; ledger entry dù long nhưng tổng trả qua
        // service sẽ convert khi cần BR-NEW-10 (threshold BVC 500.000).
        var entries = await _db.BvcLedgerEntries
            .Where(e => e.UserId == userId
                && e.Type == LedgerEntryType.DepositForfeit
                && e.CreatedAt >= since)
            .Select(e => e.Amount)
            .ToListAsync();
        long sum = 0;
        foreach (var amount in entries)
        {
            checked { sum += amount; }
        }
        return sum;
    }

    public async Task<int> CountByTypeSinceAsync(Guid userId, LedgerEntryType type, DateTime since)
    {
        return await _db.BvcLedgerEntries
            .CountAsync(e => e.UserId == userId && e.Type == type && e.CreatedAt >= since);
    }

    /// <summary>
    /// W-04: Tính tổng amount theo loại entry cho user để reconcile ví.
    /// </summary>
    public async Task<long> SumAmountByTypesAsync(Guid userId, IEnumerable<LedgerEntryType> types)
    {
        var typeList = types.ToList();
        var entries = await _db.BvcLedgerEntries
            .Where(e => e.UserId == userId && typeList.Contains(e.Type))
            .Select(e => e.Amount)
            .ToListAsync();

        long sum = 0;
        foreach (var amount in entries)
        {
            checked { sum += amount; }
        }
        return sum;
    }

    public Task AddAsync(BvcLedgerEntry entry)
    {
        _db.BvcLedgerEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}
