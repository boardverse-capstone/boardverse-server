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

    // Fix #2: Find audit by gateway transaction ID for duplicate detection
    public async Task<PaymentWebhookAudit?> GetByGatewayTransactionIdAsync(
        string gatewayTransactionId, CancellationToken ct = default)
    {
        return await _db.PaymentWebhookAudits
            .AsNoTracking()
            .Where(a => a.GatewayTransactionId == gatewayTransactionId)
            .OrderByDescending(a => a.ProcessedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CountAmountMismatchAsync(DateTime since, CancellationToken ct = default)
    {
        return await _db.PaymentWebhookAudits
            .AsNoTracking()
            .Where(a => a.Result == "amount_mismatch" && a.ProcessedAt >= since)
            .CountAsync(ct);
    }

    public async Task<bool> ExistsForDuplicateAsync(
        string? orderId, string? gatewayTransactionId, CancellationToken ct = default)
    {
        // Match nếu có cùng GatewayTransactionId (preferred) hoặc cùng OrderId
        // mà GatewayTransactionId trống (legacy SePay không gửi txn id).
        if (!string.IsNullOrWhiteSpace(gatewayTransactionId))
        {
            return await _db.PaymentWebhookAudits
                .AnyAsync(a => a.GatewayTransactionId == gatewayTransactionId, ct);
        }

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            return await _db.PaymentWebhookAudits
                .AnyAsync(a => a.OrderId == orderId, ct);
        }

        return false;
    }
}