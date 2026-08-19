using BoardVerse.Core.Entities;

namespace BoardVerse.Core.Entities;

/// <summary>
/// POS QR token lưu DB để player scan check-in (BR §21A.7 — 2 chiều).
/// Staff bấm "Tạo QR mời khách scan" → lưu token vào DB kèm reservationId (optional).
/// Player app scan token → server lookup → check-in vào cùng reservation.
/// TTL mặc định 30 phút (configurable). Sau khi consumed thì set <see cref="ConsumedAt"/>.
/// </summary>
public class PosCheckInToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK Cafe. Index để query active tokens cho cafe.</summary>
    public Guid CafeId { get; set; }

    /// <summary>
    /// FK Reservation (nullable — dự phòng walk-in flow trong tương lai).
    /// MVP: token luôn có ReservationId, scope vào reservation cụ thể.
    /// </summary>
    public Guid? ReservationId { get; set; }

    /// <summary>16-char alphanumeric uppercase (exclude 0/1/I/O). Unique.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Staff tạo token (FK User). Audit trail.</summary>
    public Guid CreatedByStaffId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    /// <summary>Staff chủ động revoke (vd chuyển ca).</summary>
    public bool IsRevoked { get; set; }

    /// <summary>Set khi player scan thành công. Idempotent replay guard.</summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>UserId của player đã scan.</summary>
    public Guid? ConsumedByUserId { get; set; }

    /// <summary>ActiveSessionId sinh ra sau khi check-in thành công.</summary>
    public Guid? ResultActiveSessionId { get; set; }

    // === Navigation ===
    public virtual Cafe? Cafe { get; set; }
    public virtual Reservation? Reservation { get; set; }
    public virtual User? CreatedByStaff { get; set; }
    public virtual User? ConsumedByUser { get; set; }
}