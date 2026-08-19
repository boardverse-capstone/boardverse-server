using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class PosCheckInTokenRepository : IPosCheckInTokenRepository
{
    private readonly BoardVerseDbContext _db;

    public PosCheckInTokenRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<PosCheckInToken?> GetByIdAsync(Guid id)
    {
        return await _db.PosCheckInTokens
            .Include(t => t.Reservation)
            .Include(t => t.Cafe)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<PosCheckInToken?> GetByTokenAsync(string token)
    {
        var normalized = token.Trim().ToUpperInvariant();
        return await _db.PosCheckInTokens
            .Include(t => t.Reservation)
            .Include(t => t.Cafe)
            .FirstOrDefaultAsync(t => t.Token == normalized);
    }

    public async Task<List<PosCheckInToken>> GetActiveTokensForCafeAsync(Guid cafeId)
    {
        var now = DateTime.UtcNow;
        return await _db.PosCheckInTokens
            .Where(t => t.CafeId == cafeId && !t.IsRevoked && t.ExpiresAt > now && t.ConsumedAt == null)
            .ToListAsync();
    }

    public async Task AddAsync(PosCheckInToken token)
    {
        await _db.PosCheckInTokens.AddAsync(token);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> TokenExistsAsync(string token)
    {
        var normalized = token.Trim().ToUpperInvariant();
        return await _db.PosCheckInTokens.AnyAsync(t => t.Token == normalized);
    }
}