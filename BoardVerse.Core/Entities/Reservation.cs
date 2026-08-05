using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Bản ghi giữ chỗ ngồi + game copy + BVC hold (BR-REQUIRED §17.4 + §19.2).
/// Được tạo atomically cùng Lobby trong 1 transaction khi confirm reservation.
/// </summary>
public class Reservation
{
    public Guid Id { get; set; }

    /// <summary>BR-DEPOSIT-01: host trả toàn bộ cọc.</summary>
    public Guid HostId { get; set; }

    public Guid CafeId { get; set; }
    public Guid GameId { get; set; }

    /// <summary>BR-NEW-04: ngày dự kiến chơi.</summary>
    public DateOnly PlayDate { get; set; }

    /// <summary>BR-NEW-15: khung giờ cố định.</summary>
    public TimeSlot TimeSlot { get; set; }

    /// <summary>BR-NEW-15b: optional, phải nằm trong [timeSlot.startTime, timeSlot.endTime].</summary>
    public TimeOnly? PreferredStartTime { get; set; }

    /// <summary>BR-LOBBY-01: scheduledTime - leadTimeMinutes (mặc định 20 phút).</summary>
    public DateTime RecruitmentDeadline { get; set; }

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }

    /// <summary>Snapshot cấu hình cọc tại thời điểm tạo (§19.1 + 21F.9).</summary>
    public DepositSnapshot DepositConfigSnapshot { get; set; } = new();

    /// <summary>BVC cuối cùng đã hold (≥ MinDepositApplied).</summary>
    public long DepositAmount { get; set; }

    /// <summary>minDeposit theo khoảng cách playDate (BR-NEW-01 §8).</summary>
    public long MinDepositApplied { get; set; }

    /// <summary>Risk multiplier snapshot tại thời điểm tạo (BR-RISK-03).</summary>
    public decimal RiskMultiplier { get; set; } = 1.0m;

    public ReservationStatus Status { get; set; } = ReservationStatus.Holding;

    /// <summary>Mirror số người hiện tại của lobby — giúp scheduler đọc nhanh.</summary>
    public int CurrentPlayers { get; set; } = 1;

    /// <summary>FK Lobby — đặt sau khi insert lobby trong cùng transaction.</summary>
    public Guid? LobbyId { get; set; }

    /// <summary>FK SeatInventory row đã hold.</summary>
    public Guid? SeatInventoryId { get; set; }

    /// <summary>FK GameInventory row đã hold.</summary>
    public Guid? GameInventoryId { get; set; }

    /// <summary>Idempotency key của request confirm (BR-REQUIRED §17.1).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// BR §21A.7: Mã share 8 ký tự (alphanumeric uppercase) dùng cho POS scan QR check-in.
    /// Unique trong hệ thống. Sinh tự động lúc tạo reservation (ConfirmAsync).
    /// </summary>
    public string ReservationCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// ScheduledTime = playDate + timeSlot.startTime — dùng cho BR-REFUND-02.
    /// Mapping (BR-NEW-15, cập nhật cover 24h):
    /// <list type="bullet">
    /// <item><description><c>Morning</c>: playDate 08:00.</description></item>
    /// <item><description><c>Afternoon</c>: playDate 13:00.</description></item>
    /// <item><description><c>Evening</c>: playDate 18:00.</description></item>
    /// <item><description><c>Night</c>: playDate 00:00 (qua đêm, scheduledStart = playDate 00:00, endTime = playDate+1 08:00).</description></item>
    /// </list>
    /// </summary>
    public DateTime ScheduledTime => PlayDate.ToDateTime(
        TimeSlot switch
        {
            TimeSlot.Morning => new TimeOnly(8, 0),
            TimeSlot.Afternoon => new TimeOnly(13, 0),
            TimeSlot.Evening => new TimeOnly(18, 0),
            TimeSlot.Night => new TimeOnly(0, 0),
            _ => new TimeOnly(8, 0)
        });

    public virtual User? Host { get; set; }
    public virtual Cafe? Cafe { get; set; }
    public virtual GameTemplate? Game { get; set; }
    public virtual Lobby? Lobby { get; set; }
    public virtual SeatInventory? SeatInventory { get; set; }
    public virtual GameInventory? GameInventory { get; set; }
}