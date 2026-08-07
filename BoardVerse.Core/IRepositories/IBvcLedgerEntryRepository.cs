using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

public interface IBvcLedgerEntryRepository
{
    /// <summary>Lấy entry theo IdempotencyKey. Trả null nếu chưa có (dùng cho BR § XVII.1).</summary>
    Task<BvcLedgerEntry?> GetByIdempotencyKeyAsync(string idempotencyKey);

    /// <summary>
    /// GAP #13 fix: Lấy entry theo IdempotencyKey với <c>FOR UPDATE</c> lock.
    /// Phải gọi trong 1 transaction. Trả null nếu entry chưa có — sau đó insert mới với cùng key (race-safe).
    /// </summary>
    Task<BvcLedgerEntry?> GetByIdempotencyKeyForUpdateAsync(string idempotencyKey);

    Task<BvcLedgerEntry?> GetByIdAsync(Guid id);

    Task<IReadOnlyList<BvcLedgerEntry>> GetHistoryAsync(Guid userId, int page, int pageSize);

    Task<int> CountByUserAsync(Guid userId);

    Task<decimal> SumForfeitAsync(Guid userId, DateTime since);

    Task<int> CountByTypeSinceAsync(Guid userId, LedgerEntryType type, DateTime since);

    /// <summary>
    /// W-04: Tính tổng amount theo loại entry cho user để reconcile ví.
    /// Credits: TopUp + AdminCredit.
    /// Debits: DepositHold + AdminDebit + DepositCapture + DepositForfeit.
    /// </summary>
    Task<long> SumAmountByTypesAsync(Guid userId, IEnumerable<LedgerEntryType> types);

    Task AddAsync(BvcLedgerEntry entry);

    Task SaveChangesAsync();
}
