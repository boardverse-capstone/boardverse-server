using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <summary>
/// EF Core implementation cho <see cref="IMemberDepositAuditLogRepository"/>.
/// M1 / Option A - docs/design/host-deposit-discount-and-bvc-payment-design.md §B1.5.
/// </summary>
public class MemberDepositAuditLogRepository : IMemberDepositAuditLogRepository
{
    private readonly BoardVerseDbContext _db;

    public MemberDepositAuditLogRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<MemberDepositAuditLog> AddAsync(
        MemberDepositAuditLog log,
        CancellationToken cancellationToken = default)
    {
        // BR § XVII.1: Idempotency — nếu key đã tồn tại, trả về row cũ (không throw).
        var existing = await FindByIdempotencyKeyAsync(log.IdempotencyKey, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        await _db.MemberDepositAuditLogs.AddAsync(log, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return log;
    }

    public Task<IReadOnlyList<MemberDepositAuditLog>> GetByMemberIdAsync(
        Guid memberId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        return _db.MemberDepositAuditLogs
            .AsNoTracking()
            .Where(l => l.MemberId == memberId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ContinueWith<IReadOnlyList<MemberDepositAuditLog>>(t => t.Result, cancellationToken);
    }

    public Task<IReadOnlyList<MemberDepositAuditLog>> GetByUserIdAsync(
        Guid userId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        return _db.MemberDepositAuditLogs
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ContinueWith<IReadOnlyList<MemberDepositAuditLog>>(t => t.Result, cancellationToken);
    }

    public Task<IReadOnlyList<MemberDepositAuditLog>> GetByActionAndTimeRangeAsync(
        string action,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        return _db.MemberDepositAuditLogs
            .AsNoTracking()
            .Where(l => l.Action == action && l.CreatedAt >= from && l.CreatedAt <= to)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken)
            .ContinueWith<IReadOnlyList<MemberDepositAuditLog>>(t => t.Result, cancellationToken);
    }

    public Task<MemberDepositAuditLog?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return _db.MemberDepositAuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.IdempotencyKey == idempotencyKey, cancellationToken);
    }
}