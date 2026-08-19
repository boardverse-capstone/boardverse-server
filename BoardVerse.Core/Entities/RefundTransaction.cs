using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// BR-REFUND-01..07 (docs/time-slot-fixed-end-design (1).md §9.4):
/// Bản ghi ghi nhận refund transaction khi reservation kết thúc (early checkout, no-show, cancel, staff override).
/// Ledger entry type tương ứng: DEPOSIT_RELEASE (refund về availableBalance) hoặc DEPOSIT_FORFEIT (forfeit → settlement).
/// </summary>
public class RefundTransaction
{
    public Guid Id { get; set; }

    /// <summary>FK Reservation.</summary>
    public Guid ReservationId { get; set; }

    /// <summary>Snapshot DepositAmount tại thời điểm tạo reservation.</summary>
    public long OriginalDeposit { get; set; }

    /// <summary>Số BVC thực sự refund về host availableBalance.</summary>
    public long RefundAmount { get; set; }

    /// <summary>Số BVC forfeit (capture về doanh thu quán hoặc forfeit pool).</summary>
    public long ForfeitAmount { get; set; }

    /// <summary>PlayedRatio tại thời điểm end session (0.0-1.0).</summary>
    public decimal? PlayedRatio { get; set; }

    /// <summary>BR-REFUND-01..07: lý do refund.</summary>
    public RefundReason Reason { get; set; }

    /// <summary>BR-REFUND-04: true nếu admin override (staff force adjust).</summary>
    public bool IsOverridden { get; set; }

    /// <summary>BR-REFUND-04: UserId staff/admin override (nullable nếu auto).</summary>
    public Guid? OverriddenBy { get; set; }

    /// <summary>BR-REFUND-04: lý do override (optional).</summary>
    public string? OverrideReason { get; set; }

    /// <summary>Trạng thái refund (Pending → Processing → Completed/Failed).</summary>
    public RefundStatus Status { get; set; } = RefundStatus.Pending;

    /// <summary>BR-§XVII.1: Idempotency key cho refund transaction.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public virtual Reservation? Reservation { get; set; }
}