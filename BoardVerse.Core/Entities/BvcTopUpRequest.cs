using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Tracking riêng cho top-up flow. Mục đích:
///  - Lookup theo OrderId khi webhook SePay đến (nhanh hơn scan ledger).
///  - Idempotency ở tầng OrderId (ngoài IdempotencyKey ở ledger).
///  - Audit: biết top-up nào pending / paid / expired / failed.
/// </summary>
public class BvcTopUpRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>OrderId gửi cho SePay (prefix BVC-...). UNIQUE.</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Số VND khách phải chuyển.</summary>
    public long AmountVnd { get; set; }

    /// <summary>Số BVC sẽ cộng (AmountVnd / 1000).</summary>
    public long ExpectedBvc { get; set; }

    /// <summary>Idempotency key do client cung cấp (UNIQUE).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public BvcTopUpStatus Status { get; set; } = BvcTopUpStatus.Pending;

    /// <summary>Ledger entry id sau khi SePay success + cộng ví.</summary>
    public Guid? LedgerEntryId { get; set; }

    /// <summary>Mã SePay transaction id (nhận từ webhook).</summary>
    public string? GatewayTransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
}
