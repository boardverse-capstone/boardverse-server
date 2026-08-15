using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// GAP-10 Fix: Repository cho PaymentWebhookAudit table.
/// Cho phép admin query lịch sử webhook để debug/refund.
/// </summary>
public interface IPaymentWebhookAuditRepository
{
    /// <summary>
    /// Ghi 1 audit record mới.
    /// </summary>
    Task AddAsync(PaymentWebhookAudit audit, CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách audit theo SessionId (cho admin drill-down).
    /// </summary>
    Task<IReadOnlyList<PaymentWebhookAudit>> GetBySessionIdAsync(
        Guid sessionId, int take = 50, CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách audit theo OrderId.
    /// </summary>
    Task<IReadOnlyList<PaymentWebhookAudit>> GetByOrderIdAsync(
        string orderId, CancellationToken ct = default);

    /// <summary>
    /// GAP-11 Fix: Đếm số lượng amount_mismatch trong 1 khoảng thời gian.
    /// Alert nếu > threshold → có thể là attack hoặc bug.
    /// </summary>
    Task<int> CountAmountMismatchAsync(DateTime since, CancellationToken ct = default);
}