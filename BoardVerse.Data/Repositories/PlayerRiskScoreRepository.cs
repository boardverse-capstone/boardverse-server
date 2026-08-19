using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <inheritdoc cref="IPlayerRiskScoreRepository"/>
public class PlayerRiskScoreRepository : IPlayerRiskScoreRepository
{
    private readonly BoardVerseDbContext _db;

    public PlayerRiskScoreRepository(BoardVerseDbContext db) => _db = db;

    public Task<PlayerRiskScore?> GetByUserIdAsync(Guid userId) =>
        _db.PlayerRiskScores.FirstOrDefaultAsync(s => s.UserId == userId);

    public async Task UpsertAsync(PlayerRiskScore snapshot)
    {
        // BR-RISK-01: 1 user chỉ có 1 row. Upsert bằng cách check existing.
        var existing = await _db.PlayerRiskScores
            .FirstOrDefaultAsync(s => s.UserId == snapshot.UserId);

        if (existing == null)
        {
            await _db.PlayerRiskScores.AddAsync(snapshot);
        }
        else
        {
            existing.RiskScore = snapshot.RiskScore;
            existing.RiskLevel = snapshot.RiskLevel;
            existing.Signals = snapshot.Signals;
            existing.LastUpdated = snapshot.LastUpdated;
            // Preserve AdminNote / AdminAction — không overwrite bằng job tự động.
        }
    }

    public async Task AppendHistoryAsync(RiskScoreHistory history)
    {
        await _db.RiskScoreHistories.AddAsync(history);
    }

    public async Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(int batchSize, int skip)
    {
        // BR-RISK-01: Tính cho users có Wallet (đã onboard BVC).
        return await _db.Wallets
            .AsNoTracking()
            .OrderBy(w => w.UserId)
            .Skip(skip)
            .Take(batchSize)
            .Select(w => w.UserId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<RiskScoreHistory>> GetHistoryByUserIdAndDateRangeAsync(Guid userId, DateOnly fromDate, DateOnly toDate) =>
        await _db.RiskScoreHistories
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.SnapshotDate >= fromDate && h.SnapshotDate <= toDate)
            .OrderBy(h => h.SnapshotDate)
            .ToListAsync();
}
