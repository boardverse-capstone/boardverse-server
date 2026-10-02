using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Audit log cho mọi hành động liên quan đến per-member BVC deposit.
/// M1 / Option A - docs/design/host-deposit-discount-and-bvc-payment-design.md §B1.5.
///
/// Append-only (BR § XVII.6): không UPDATE/DELETE. Mỗi action tạo row mới.
/// Cho phép admin / finance team truy vết lịch sử deposit của từng member.
/// </summary>
/// <remarks>
/// <b>Action values (string, không dùng enum để linh hoạt):</b>
/// <list type="bullet">
///   <item><c>Refunded_OnMerge</c> — Member merge sang lobby khác (Exception 4), deposit refund về wallet.</item>
///   <item><c>Refunded_OnSessionCancel</c> — Session cancel sau khi deposit applied.</item>
///   <item><c>Captured_OnGroupAPay</c> — Host chọn DiscountGroup, deposit được phân bổ & capture.</item>
///   <item><c>Captured_OnEarlyCheckout</c> — Member early checkout, deposit của member đó capture.</item>
///   <item><c>Skipped_NoActiveMembers</c> — Session Pay nhưng không có active member (all merged).</item>
///   <item><c>Skipped_LobbyTerminal</c> — Lobby Closed/Cancelled → skip discount.</item>
/// </list>
/// </remarks>
public class MemberDepositAuditLog
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to <see cref="ActiveSessionMember.Id"/>.</summary>
    public Guid MemberId { get; set; }

    /// <summary>
    /// Snapshot UserId của member tại thời điểm tạo (Guest_Slot có UserId = null nhưng vẫn log).
    /// Denormalized để query độc lập với member entity (audit trail phải survive kể cả khi member bị xoá).
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Loại action: <c>Refunded_OnMerge</c>, <c>Captured_OnGroupAPay</c>, etc.
    /// Xem remarks ở class summary.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Số BVC liên quan (luôn dương — direction quyết định bởi <see cref="Action"/>).</summary>
    public long AmountBvc { get; set; }

    /// <summary>FK to BookingDeposit / Reservation deposit (nullable cho TopUp).</summary>
    public Guid? DepositId { get; set; }

    /// <summary>Lobby gốc (merge source).</summary>
    public Guid? FromLobbyId { get; set; }

    /// <summary>Session gốc (merge source).</summary>
    public Guid? FromSessionId { get; set; }

    /// <summary>Lobby đích (merge target).</summary>
    public Guid? ToLobbyId { get; set; }

    /// <summary>Session đích (merge target).</summary>
    public Guid? ToSessionId { get; set; }

    /// <summary>Thời điểm merge xảy ra (chỉ apply cho action = Refunded_OnMerge).</summary>
    public DateTime? MergedAt { get; set; }

    /// <summary>Lý do chi tiết (max 500 ký tự). Ví dụ: "A3 leave early, deposit consumed before merge".</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Idempotency key (UNIQUE ở DB). BR § XVII.1: cùng key + payload → không tạo row mới.
    /// Format gợi ý: <c>mem-deposit-{MemberId}-{Action}-{Timestamp}</c>.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>FK to <see cref="BvcLedgerEntry.Id"/> của transaction liên quan (nếu có).</summary>
    public Guid? LedgerEntryId { get; set; }

    /// <summary>FK to User.Id của staff/host/admin thực hiện action.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Timestamp tạo row.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    public virtual ActiveSessionMember Member { get; set; } = null!;
    public virtual User? User { get; set; }
}