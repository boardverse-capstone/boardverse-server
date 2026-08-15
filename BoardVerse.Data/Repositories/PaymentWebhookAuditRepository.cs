using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

/// <inheritdoc cref="IPaymentWebhookAuditRepository"/>
public class PaymentWebhookAuditRepository : IPaymentWebhookAuditRepository
{
    private readonly BoardVerseDbContext _db;

    public PaymentWebhookAuditRepository(BoardVerseDbContext db) => _db = db;

    public async Task AddAsync(PaymentWebhookAudit audit, CancellationToken ct = default)
    {
        await _db.PaymentWebhookAudits.AddAsync(audit, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentWebhookAudit>> GetBySessionIdAsync(
        Guid sessionId, int take = 50, CancellationToken ct = default)
    {
        return await _db.PaymentWebhookAudits
            .AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderByDescending(a => a.ProcessedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentWebhookAudit>> GetByOrderIdAsync(
        string orderId, CancellationToken ct = default)
    {
        return await _db.PaymentWebhookAudits
            .AsNoTracking()
            .Where(a => a.OrderId == orderId)
            .OrderByDescending(a => a.ProcessedAt)
            .ToListAsync(ct);
    }

    public async Task<int> CountAmountMismatchAsync(DateTime since, CancellationToken ct = default)
    {
        return await _db.PaymentWebhookAudits
            .AsNoTracking()
            .Where(a => a.Result == "amount_mismatch" && a.ProcessedAt >= since)
            .CountAsync(ct);
    }
}