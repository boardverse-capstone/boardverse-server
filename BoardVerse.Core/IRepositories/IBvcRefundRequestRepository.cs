using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

public interface IBvcRefundRequestRepository
{
    Task<BvcRefundRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR § XVII.1: Lookup theo IdempotencyKey (UNIQUE ở DB).
    /// </summary>
    Task<BvcRefundRequest?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy refund request kèm ledger entry liên kết (admin xem chi tiết).
    /// </summary>
    Task<BvcRefundRequest?> GetByIdWithLedgerEntryAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy refund request với FOR UPDATE lock — dùng khi admin resolve để tránh race.
    /// </summary>
    Task<BvcRefundRequest?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách refund request cho admin dashboard (phân trang + filter).
    /// </summary>
    Task<(IReadOnlyList<BvcRefundRequest> Items, int TotalCount)> GetPagedAsync(
        RefundRequestStatus? statusFilter,
        Guid? userIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy lịch sử refund của 1 player (phân trang).
    /// </summary>
    Task<(IReadOnlyList<BvcRefundRequest> Items, int TotalCount)> GetByUserIdPagedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task AddAsync(BvcRefundRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(BvcRefundRequest request);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}