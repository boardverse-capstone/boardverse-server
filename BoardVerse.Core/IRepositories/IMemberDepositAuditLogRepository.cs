using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho <see cref="MemberDepositAuditLog"/>.
/// M1 / Option A - docs/design/host-deposit-discount-and-bvc-payment-design.md §B1.5.
/// </summary>
public interface IMemberDepositAuditLogRepository
{
    /// <summary>
    /// Thêm row mới vào audit log.
    /// Idempotent theo <see cref="MemberDepositAuditLog.IdempotencyKey"/> (UNIQUE ở DB).
    /// Nếu key đã tồn tại → trả về row cũ (không throw).
    /// </summary>
    /// <returns>Row đã insert (hoặc row cũ nếu idempotency hit).</returns>
    Task<MemberDepositAuditLog> AddAsync(MemberDepositAuditLog log, CancellationToken cancellationToken = default);

    /// <summary>
    /// Query log theo MemberId, sắp xếp mới nhất trước.
    /// Dùng cho UI "lịch sử deposit của member".
    /// </summary>
    Task<IReadOnlyList<MemberDepositAuditLog>> GetByMemberIdAsync(
        Guid memberId,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query log theo UserId (snapshot), sắp xếp mới nhất trước.
    /// Dùng cho admin/finance trace lịch sử deposit của 1 player qua nhiều session.
    /// </summary>
    Task<IReadOnlyList<MemberDepositAuditLog>> GetByUserIdAsync(
        Guid userId,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query log theo Action + time range (dùng cho reporting).
    /// Ví dụ: tất cả refund merge trong 7 ngày qua.
    /// </summary>
    Task<IReadOnlyList<MemberDepositAuditLog>> GetByActionAndTimeRangeAsync(
        string action,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check idempotency — trả về row nếu đã tồn tại, null nếu chưa.
    /// </summary>
    Task<MemberDepositAuditLog?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}