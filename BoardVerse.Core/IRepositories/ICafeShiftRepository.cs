using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface ICafeShiftRepository
{
    Task AddAsync(CafeShift shift, CancellationToken cancellationToken = default);
    Task UpdateAsync(CafeShift shift, CancellationToken cancellationToken = default);
    Task<CafeShift?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CafeShift?> GetCurrentOpenShiftAsync(Guid cafeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CafeShift>> GetHistoryAsync(Guid cafeId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> GetHistoryCountAsync(Guid cafeId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-CAFE-SHIFT-01 reconciliation: SUM TotalAmount + COUNT(*) của các phiên chơi đã
    /// Paid (Status = 3) thuộc quán, với PaidAt nằm trong cửa sổ [fromUtc, toUtc].
    /// Dùng cho <c>CafeShiftService.RecalculateShiftTotalsAsync</c>.
    /// Chỉ load 2 scalar (Count, Sum) — không hydrate full entities, không navigation.
    /// </summary>
    /// <param name="cafeId">Mã quán cần query.</param>
    /// <param name="fromUtc">Bắt đầu cửa sổ (thường = shift.OpenedAt).</param>
    /// <param name="toUtc">Kết thúc cửa sổ (thường = shift.ClosedAt hoặc DateTime.UtcNow nếu shift còn Open).</param>
    /// <param name="cancellationToken">Token huỷ.</param>
    /// <returns>Tuple (paidSessionCount, paidRevenueTotal).</returns>
    Task<(int PaidSessionCount, decimal PaidRevenueTotal)> SumPaidSessionsByCafeInRangeAsync(
        Guid cafeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
