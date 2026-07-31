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

    public async Task<CafeTable?> GetByIdAsync(Guid tableId)
    {
        return await _db.CafeTables.FirstOrDefaultAsync(t => t.Id == tableId);
    }

    public async Task<IReadOnlyList<CafeTable>> GetByCafeIdAsync(Guid cafeId)
    {
        return await _db.CafeTables
            .Where(t => t.CafeId == cafeId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync();
    }
}
