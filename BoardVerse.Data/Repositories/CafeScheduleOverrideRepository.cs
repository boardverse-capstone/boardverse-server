using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// Repository cho CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public class CafeScheduleOverrideRepository : ICafeScheduleOverrideRepository
{
    private readonly BoardVerseDbContext _db;

    public CafeScheduleOverrideRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<CafeScheduleOverride?> GetByApplyDateAsync(Guid cafeId, DateOnly applyDate)
    {
        return _db.CafeScheduleOverrides
            .FirstOrDefaultAsync(o => o.CafeId == cafeId && o.ApplyDate == applyDate);
    }

    public async Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId)
    {
        return await _db.CafeScheduleOverrides
            .Where(o => o.CafeId == cafeId)
            .OrderBy(o => o.ApplyDate)
            .ToListAsync();
    }

    public Task AddAsync(CafeScheduleOverride overrideEntity)
    {
        _db.CafeScheduleOverrides.Add(overrideEntity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CafeScheduleOverride overrideEntity)
    {
        overrideEntity.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public async Task DeleteByIdAsync(Guid overrideId)
    {
        var existing = await _db.CafeScheduleOverrides
            .FirstOrDefaultAsync(o => o.Id == overrideId);
        if (existing != null)
        {
            _db.CafeScheduleOverrides.Remove(existing);
        }
    }

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}
