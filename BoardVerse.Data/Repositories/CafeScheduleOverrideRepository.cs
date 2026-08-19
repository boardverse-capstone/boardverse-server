using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class CafeScheduleOverrideRepository : ICafeScheduleOverrideRepository
{
    private readonly BoardVerseDbContext _db;

    public CafeScheduleOverrideRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<CafeScheduleOverride?> GetActiveAsync(Guid cafeId, TimeSlot slot, DateOnly playDate)
    {
        return _db.CafeScheduleOverrides
            .Where(o => o.CafeId == cafeId && o.TimeSlot == slot)
            .Where(o => o.EffectiveFrom == null || o.EffectiveFrom <= playDate)
            .Where(o => o.EffectiveTo == null || o.EffectiveTo >= playDate)
            .OrderByDescending(o => o.UpdatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId)
    {
        return await _db.CafeScheduleOverrides
            .Where(o => o.CafeId == cafeId)
            .OrderBy(o => o.TimeSlot)
            .ToListAsync();
    }

    public Task<CafeScheduleOverride?> GetByCafeAndSlotAsync(Guid cafeId, TimeSlot slot)
    {
        return _db.CafeScheduleOverrides
            .FirstOrDefaultAsync(o => o.CafeId == cafeId && o.TimeSlot == slot);
    }

    public Task AddAsync(CafeScheduleOverride overrideEntity)
    {
        _db.CafeScheduleOverrides.Add(overrideEntity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CafeScheduleOverride overrideEntity)
    {
        // Entity đã được EF tracking (qua GetByCafeAndSlotAsync). Chỉ cập nhật UpdatedAt —
        // EF sẽ tự detect property changes khi SaveChangesAsync được gọi.
        overrideEntity.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid cafeId, TimeSlot slot)
    {
        var existing = await _db.CafeScheduleOverrides
            .FirstOrDefaultAsync(o => o.CafeId == cafeId && o.TimeSlot == slot);
        if (existing != null)
        {
            _db.CafeScheduleOverrides.Remove(existing);
        }
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
