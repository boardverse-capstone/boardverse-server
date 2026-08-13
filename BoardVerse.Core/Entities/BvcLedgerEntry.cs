using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Bản ghi sổ cái BVC — append-only, không bao giờ UPDATE/DELETE một dòng đã ghi
/// (BR § III.3). Đảm bảo audit & tính lại số dư từ ledger.
/// </summary>
public class BvcLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Loại entry quyết định tác động lên 2 số dư.</summary>
    public LedgerEntryType Type { get; set; }

    /// <summary>Số BVC. Luôn dương — direction quyết định bởi <see cref="Type"/>.</summary>
    public long Amount { get; set; }

    /// <summary>Mã booking liên kết (nếu có). Nullable với TopUp / Adjustment.
    /// TD-02: Legacy — preference là <c>RelatedReservationId</c>. Giữ để backward compat.
    /// [Obsolete("§9.7: Prefer RelatedReservationId — sẽ chuyển sang khi migrate Booking→Reservation.")]
    public Guid? RelatedBookingId { get; set; }

    /// <summary>TD-02: Mã Reservation liên kết — nguồn chính cho BVC ledger entries mới.
    /// Phase 1 thêm nullable này để BVC ledger hoạt động với Reservation flow mới.</summary>
    public Guid? RelatedReservationId { get; set; }

    /// <summary>Mã lobby liên kết (nếu có). Nullable với TopUp / Adjustment.</summary>
    public Guid? RelatedLobbyId { get; set; }

    /// <summary>Mã tham chiếu thanh toán gateway (vd: SePayOrderId). Nullable.</summary>
    public string? RelatedPaymentRef { get; set; }

    /// <summary>
    /// Idempotency key do client cung cấp. UNIQUE ở DB.
    /// BR § XVII.1: cùng key + payload → trả kết quả cũ, không ghi entry mới.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary><see cref="Wallet.AvailableBalance"/> sau giao dịch này.</summary>
    public long BalanceSnapshot { get; set; }

    /// <summary>Ghi chú ngữ cảnh (lý do adjustment / mã top-up / ...). Optional.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
    // TD-02: Navigation `Reservation` đã bỏ — EF shadow FK gây 42703. Dùng RelatedReservationId scalar lookup nếu cần.
}
