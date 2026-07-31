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
    public Guid LobbyId { get; set; }
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

    // === Navigation ===
    public virtual Lobby Lobby { get; set; } = null!;
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual CafeTable CafeTable { get; set; } = null!;
    /// <summary>BR-05: Navigation đến BookingDeposit (chỉ Host mới đặt cọc).</summary>
    public virtual BookingDeposit? BookingDeposit { get; set; }
}
