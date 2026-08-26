using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface IBvcLedgerEntryRepository
{
    /// <summary>Lấy entry theo IdempotencyKey. Trả null nếu chưa có (dùng cho BR § XVII.1).</summary>
    Task<BvcLedgerEntry?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// GAP #13 fix: Lấy entry theo IdempotencyKey với <c>FOR UPDATE</c> lock.
    /// Phải gọi trong 1 transaction. Trả null nếu entry chưa có — sau đó insert mới với cùng key (race-safe).
    /// </summary>
    Task<BvcLedgerEntry?> GetByIdempotencyKeyForUpdateAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    Task<BvcLedgerEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BvcLedgerEntry>> GetHistoryAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<decimal> SumForfeitAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default);

    Task<int> CountByTypeSinceAsync(Guid userId, LedgerEntryType type, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// W-04: Tính tổng amount theo loại entry cho user để reconcile ví.
    /// Credits: TopUp + AdminCredit.
    /// Debits: DepositHold + AdminDebit + DepositCapture + DepositForfeit.
    /// </summary>
    Task<long> SumAmountByTypesAsync(Guid userId, IEnumerable<LedgerEntryType> types, CancellationToken cancellationToken = default);

    Task AddAsync(BvcLedgerEntry entry, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
