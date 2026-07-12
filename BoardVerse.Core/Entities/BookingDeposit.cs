using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Đơn cọc trực tuyến dùng cho phiên chơi.
/// Khi phiên chơi kết thúc, BoardVerse sẽ chuyển khoản số tiền này
/// từ master account sang tài khoản ngân hàng của cafe manager.
/// </summary>
public class BookingDeposit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OrderId { get; set; } = string.Empty;
    /// <summary>ActiveSession liên kết. Nullable vì deposit tạo TRƯỚC khi check-in.</summary>
    public Guid? ActiveSessionId { get; set; }
    public Guid CafeId { get; set; }
    public Guid CafeManagerId { get; set; }
    public Guid? MasterAccountId { get; set; }
    public decimal Amount { get; set; }
    public DepositRefundPolicy RefundPolicy { get; set; }
    public BookingDepositStatus Status { get; set; } = BookingDepositStatus.Pending;
    public string? TransferContent { get; set; }
    public string? SePayTransactionId { get; set; }
    public string? SePayTransferId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime? ForfeitedAt { get; set; }
    /// <summary>Giờ hẹn chơi dự kiến — dùng để tính partial refund (BR-18).</summary>
    public DateTime? ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual PaymentMasterAccount? MasterAccount { get; set; }
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual ActiveSession? ActiveSession { get; set; }
}
