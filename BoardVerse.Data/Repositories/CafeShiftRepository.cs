using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class CafeShiftRepository : ICafeShiftRepository
{
    private readonly BoardVerseDbContext _db;

    public CafeShiftRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(CafeShift shift)
    {
        await _db.CafeShifts.AddAsync(shift);
    }

    public async Task UpdateAsync(CafeShift shift)
    {
        _db.CafeShifts.Update(shift);
        await Task.CompletedTask;
    }

    public async Task<CafeShift?> GetByIdAsync(Guid id)
    {
        return await _db.CafeShifts
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<CafeShift?> GetCurrentOpenShiftAsync(Guid cafeId)
    {
        return await _db.CafeShifts
            .Where(s => s.CafeId == cafeId && s.Status == ShiftStatus.Open)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<CafeShift>> GetHistoryAsync(Guid cafeId, int page, int pageSize)
    {
        return await _db.CafeShifts
            .Where(s => s.CafeId == cafeId)
            .OrderByDescending(s => s.OpenedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetHistoryCountAsync(Guid cafeId)
    {
        return await _db.CafeShifts
            .Where(s => s.CafeId == cafeId)
            .CountAsync();
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}
