using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Waitlist entry khi tournament đầy.
/// Khi slot mở ra, người đầu tiên trong waitlist được thông báo và có thể đăng ký.
/// </summary>
public class TournamentWaitlist
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã giải đấu.</summary>
    public Guid TournamentId { get; set; }

    /// <summary>User đăng ký vào waitlist.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Vị trí trong waitlist (1 = đầu tiên).
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Trạng thái waitlist.
    /// - Pending: đang chờ slot.
    /// - Offered: đã có slot, đang chờ user xác nhận.
    /// - Joined: user đã xác nhận và được thêm vào tournament.
    /// - Expired: hết hạn xác nhận mà không tham gia.
    /// - Cancelled: user chủ động rời waitlist.
    /// </summary>
    public TournamentWaitlistStatus Status { get; set; } = TournamentWaitlistStatus.Pending;

    /// <summary>Thời điểm user vào waitlist.</summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm được offer slot (Status = Offered).
    /// Null khi Status = Pending.
    /// </summary>
    public DateTime? OfferedAt { get; set; }

    /// <summary>
    /// Thời điểm hết hạn offer (mặc định OfferedAt + 30 phút).
    /// </summary>
    public DateTime? OfferExpiresAt { get; set; }

    /// <summary>Thời điểm user xác nhận tham gia (Status = Joined).</summary>
    public DateTime? ConfirmedAt { get; set; }

    // === Navigation ===
    public virtual Tournament Tournament { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
