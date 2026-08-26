using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class CafeTableRepository : ICafeTableRepository
{
    private readonly BoardVerseDbContext _db;

    public CafeTableRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<CafeTable?> GetByIdAsync(Guid tableId, CancellationToken cancellationToken = default)
    {
        return await _db.CafeTables.FirstOrDefaultAsync(t => t.Id == tableId, cancellationToken);
    }

    public async Task<IReadOnlyList<CafeTable>> GetByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        return await _db.CafeTables
            .Where(t => t.CafeId == cafeId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateAsync(CafeTable table, CancellationToken cancellationToken = default)
    {
        _db.CafeTables.Update(table);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}