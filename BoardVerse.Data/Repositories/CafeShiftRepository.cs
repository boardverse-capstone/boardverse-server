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

    public async Task AddAsync(CafeShift shift, CancellationToken cancellationToken = default)
    {
        await _db.CafeShifts.AddAsync(shift);
    }

    public async Task UpdateAsync(CafeShift shift, CancellationToken cancellationToken = default)
    {
        _db.CafeShifts.Update(shift);
        await Task.CompletedTask;
    }

    public async Task<CafeShift?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Eager load OpenedByUser.Profile + ClosedByUser.Profile để service build tên hiển thị
        // cho OpenedByUserName / ClosedByUserName mà không cần query phụ.
        return await _db.CafeShifts
            .Include(s => s.OpenedByUser).ThenInclude(u => u.Profile)
            .Include(s => s.ClosedByUser).ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<CafeShift?> GetCurrentOpenShiftAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        // Eager load OpenedByUser.Profile để service trả tên người mở ca trong cùng 1 query
        // (tránh N+1 khi render UI). ClosedByUser không cần — ca Open chưa có người đóng.
        return await _db.CafeShifts
            .Include(s => s.OpenedByUser).ThenInclude(u => u.Profile)
            .Where(s => s.CafeId == cafeId && s.Status == ShiftStatus.Open)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<CafeShift>> GetHistoryAsync(Guid cafeId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Eager load cả 2 navigation để history list render tên người mở/đóng ca ngay trong
        // 1 round-trip. Page nhỏ (default 10) nên chi phí Include không đáng kể.
        return await _db.CafeShifts
            .Include(s => s.OpenedByUser).ThenInclude(u => u.Profile)
            .Include(s => s.ClosedByUser).ThenInclude(u => u.Profile)
            .Where(s => s.CafeId == cafeId)
            .OrderByDescending(s => s.OpenedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetHistoryCountAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        return await _db.CafeShifts
            .Where(s => s.CafeId == cafeId)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// BR-CAFE-SHIFT-01 reconciliation: SUM TotalAmount + COUNT(*) của các phiên chơi đã
    /// Paid (Status = 3) thuộc quán, với PaidAt nằm trong cửa sổ [fromUtc, toUtc].
    /// Dùng cho <c>CafeShiftService.RecalculateShiftTotalsAsync</c>.
    /// Chỉ load 2 scalar (Count, Sum) — không hydrate full entities, không navigation.
    /// </summary>
    public async Task<(int PaidSessionCount, decimal PaidRevenueTotal)> SumPaidSessionsByCafeInRangeAsync(
        Guid cafeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        // Filter theo PaidAt (DateTime UTC) để align với shift.OpenedAt / shift.ClosedAt.
        // PaidAt nullable: cần check HasValue để khớp "paid" trong cùng window.
        // fromUtc/toUtc inclusive ở cả 2 đầu.
        var query = _db.ActiveSessions
            .Where(s => s.CafeId == cafeId
                && s.Status == GroupSessionStatus.Paid
                && s.PaidAt.HasValue
                && s.PaidAt.Value >= fromUtc
                && s.PaidAt.Value <= toUtc);

        var count = await query.CountAsync(cancellationToken);
        var sum = await query.SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;
        return (count, sum);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync();
    }
}
