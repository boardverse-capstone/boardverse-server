using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Bản ghi giải ngân/đối soát deposit từ master account về cafe manager.
/// Dùng để audit và retry khi SePay transfer fail.
/// </summary>
public class CafeSettlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CafeId { get; set; }
    public Guid CafeManagerId { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public Guid? BookingDepositId { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal? FeeAmount { get; set; }
    public decimal NetTransferAmount { get; set; }
    public string? SePayTransferId { get; set; }
    public CafeSettlementStatus Status { get; set; } = CafeSettlementStatus.Pending;
    public string? FailureReason { get; set; }
    public DateTime? TransferredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // === Retry tracking ===
    /// <summary>Số lần SePay transfer đã thử.</summary>
    public int RetryCount { get; set; }

    /// <summary>Earliest time the retry job may pick this settlement again.</summary>
    public DateTime? NextRetryAt { get; set; }
}