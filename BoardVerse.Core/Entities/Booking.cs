using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Đặt chỗ trước tại quán cafe boardgame.
/// Theo ERD: id, lobbyId, cafeId, cafeTableId, scheduledStartTime, scheduleEndTime,
///         status, verificationQRCode, playerQuantity.
/// </summary>
public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // === Relationships ===
    /// <summary>
    /// Lobby liên kết. Nullable để hỗ trợ walk-in booking (BR-22 + mobile gap #3)
    /// — player đến quán trực tiếp không qua luồng ghép đội.
    /// </summary>
    public Guid? LobbyId { get; set; }
    public Guid CafeId { get; set; }
    public Guid CafeTableId { get; set; }

    // === Schedule ===
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduleEndTime { get; set; }

    // === Status ===
    public BookingStatus Status { get; set; } = BookingStatus.PendingDeposit;

    /// <summary>QR code dùng để POS check-in xác minh booking.</summary>
    public string? VerificationQRCode { get; set; }

    /// <summary>Số người chơi trong booking.</summary>
    public int PlayerQuantity { get; set; } = 1;

    // === Check-in audit (mobile gap #4: cần cho no-show-votes time window) ===
    /// <summary>Thời điểm Staff quét QR check-in tại quán. Null nếu chưa check-in.</summary>
    public DateTime? CheckedInAt { get; set; }

    /// <summary>UserId của Staff đã thực hiện check-in.</summary>
    public Guid? CheckedInByUserId { get; set; }

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    /// <summary>Nullable cho walk-in booking (mobile gap #3).</summary>
    public virtual Lobby? Lobby { get; set; }
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual CafeTable CafeTable { get; set; } = null!;
    /// <summary>BR-05: Navigation đến BookingDeposit (chỉ Host mới đặt cọc).</summary>
    public virtual BookingDeposit? BookingDeposit { get; set; }
    /// <summary>Staff đã thực hiện check-in. Null nếu chưa check-in hoặc staff đã bị xoá.</summary>
    public virtual User? CheckedInByUser { get; set; }
}
