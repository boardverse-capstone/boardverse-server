using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Yêu cầu hoàn BVC do player gửi, admin xem xét và duyệt.
/// Dùng cho case:
///   - Lỗi hệ thống (SePay timeout, Neon down, race condition) mà auto-refund không chạy.
///   - Player nạp nhầm/nạp sai số tiền.
///   - Admin xem xét case ngoại lệ (host hủy &lt;6h vì bất khả kháng).
///
/// Một refund request bind vào 1 ledger entry cụ thể — player giải thích lý do,
/// admin duyệt sẽ ghi 1 ledger entry AdminCredit mới (append-only, BR § III.3).
///
/// BR-RISK-05: mọi admin resolve action ghi PlayerActionHistory.
/// </summary>
public class BvcRefundRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Player gửi yêu cầu.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Ledger entry liên kết (mục tiêu hoàn).
    /// Cho phép mọi entry type — admin tự đánh giá hợp lệ.
    /// </summary>
    public Guid RelatedLedgerEntryId { get; set; }

    /// <summary>
    /// Số BVC player yêu cầu hoàn (do player nhập).
    /// Admin có thể override khi approve (lưu lại vào <see cref="ApprovedAmountBvc"/>).
    /// </summary>
    public long RequestedAmountBvc { get; set; }

    /// <summary>
    /// Số BVC thực sự hoàn (admin duyệt). Null khi status = Pending/Rejected/Cancelled.
    /// </summary>
    public long? ApprovedAmountBvc { get; set; }

    /// <summary>Lý do player gửi yêu cầu (min 20 ký tự — rule).</summary>
    public string PlayerReason { get; set; } = string.Empty;

    /// <summary>
    /// Idempotency key do player cung cấp khi tạo request (BR § XVII.1).
    /// UNIQUE ở DB. Cùng key + payload → trả về request cũ, không tạo mới.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Admin ghi chú khi resolve (lý do duyệt/từ chối).</summary>
    public string? AdminNote { get; set; }

    /// <summary>Trạng thái hiện tại (BR § III.6 — lifecycle).</summary>
    public RefundRequestStatus Status { get; set; } = RefundRequestStatus.Pending;

    /// <summary>Admin userId xử lý. Null khi chưa resolve.</summary>
    public Guid? ResolvedByAdminId { get; set; }

    /// <summary>Thời điểm admin resolve. Null khi chưa resolve.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Ledger entry AdminCredit phát sinh khi admin approve (BR § III.3 append-only).
    /// Null khi chưa approve.
    /// </summary>
    public Guid? ResultLedgerEntryId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
}