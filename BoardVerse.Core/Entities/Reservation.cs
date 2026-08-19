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

    /// <summary>
    /// BR-NEW-15b: optional start time.
    /// Nếu có, phải nằm trong [timeSlot.startTime, timeSlot.endTime].
    /// </summary>
    public TimeOnly? PreferredStartTime { get; set; }

    /// <summary>
    /// Optional end time.
    /// Nếu có, phải nằm trong [PreferredStartTime, timeSlot.endTime], cùng ngày playDate.
    /// </summary>
    public TimeOnly? PreferredEndTime { get; set; }

    /// <summary>BR-LOBBY-01: scheduledTime - leadTimeMinutes (mặc định 20 phút).</summary>
    public DateTime RecruitmentDeadline { get; set; }

    /// <summary>
    /// BR-RESV-02 + BR-LOBBY-01: Scheduled start time = playDate + startTime do user chọn.
    /// Lưu DB để consistency với Quote response, tính refund policy, playedRatio.
    /// </summary>
    public DateTime ScheduledStartTime { get; set; }

    /// <summary>
    /// BR-RESV-02: Scheduled end time = playDate + endTime do user chọn, cùng ngày playDate.
    /// Lưu DB để query WalkInWindowCleanupJob (§4.4), playedRatio (§4.3),
    /// và extension flow (Phase 3) không cần derive runtime.
    /// </summary>
    public DateTime ScheduledEndTime { get; set; }

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

    /// <summary>BR-EXT-03 §3.5: Số lần extend đã thực hiện. Max 2.</summary>
    public int ExtensionCount { get; set; } = 0;

    /// <summary>BR-EXT-03 §3.5: ScheduledEndTime sau lần extend cuối cùng.
    /// Nullable: null = chưa extend, dùng ScheduledEndTime gốc.</summary>
    public DateTime? ExtendedEndTime { get; set; }

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

    // ===== BR-CHECKIN-03 + BR-END-01..05 (docs/time-slot-fixed-end-design (1).md §9.1) =====

    /// <summary>BR-CHECKIN-03: thời điểm check-in thực tế tại quán. Set khi POS scan QR.</summary>
    public DateTime? CheckedInAt { get; set; }

    /// <summary>
    /// Số bàn được staff gán khi check-in.
    /// Null nếu chưa check-in.
    /// App hiển thị "Bàn số X" trong InGameSessionPage để player biết mình ngồi ở đâu.
    /// </summary>
    public int? TableNumber { get; set; }

    /// <summary>BR-END-01: thời điểm kết thúc thực tế. Set khi POS end session.</summary>
    public DateTime? ActualEndAt { get; set; }

    /// <summary>BR-END-02: playedRatio = (ActualEndAt - CheckedInAt) / (ScheduledEndTime - ScheduledStartTime). Decimal(5,4).</summary>
    public decimal? PlayedRatio { get; set; }

    /// <summary>BR-END-01: lý do kết thúc session (OnTime/EarlyLeave/Extended/NoShow/Cancelled/StaffEnded/AutoReleased).</summary>
    public SessionEndReason? EndReason { get; set; }

    /// <summary>BR-EC-04: FK WalkInWindow được tạo khi early checkout / no-show.</summary>
    public Guid? WalkInWindowId { get; set; }

    /// <summary>BR-REFUND-04: User ID của người thực hiện cancel (host hoặc admin/staff).</summary>
    public Guid? CancelledBy { get; set; }

    /// <summary>BR-REFUND-04: lý do cancel (max 500 ký tự).</summary>
    public string? CancelReason { get; set; }

    // Navigation property cho WalkInWindow.
    public virtual WalkInWindow? WalkInWindow { get; set; }

    public virtual User? Host { get; set; }
    public virtual Cafe? Cafe { get; set; }
    public virtual GameTemplate? Game { get; set; }
    public virtual Lobby? Lobby { get; set; }
    public virtual SeatInventory? SeatInventory { get; set; }
    public virtual GameInventory? GameInventory { get; set; }
    // TD-02: Navigation cho Rating/NoShowVote trên Reservation (nullable cho legacy rows chỉ có BookingId)
    public virtual ICollection<BookingRating> Ratings { get; set; } = [];
    public virtual ICollection<BookingNoShowVote> NoShowVotes { get; set; } = [];
    // TD-02: Navigation cho KarmaShortPlayRecord (§4.3 + §9.6)
    public virtual ICollection<KarmaShortPlayRecord> ShortPlayRecords { get; set; } = [];
    // TD-02: Navigation cho BvcLedgerEntry
    public virtual ICollection<BvcLedgerEntry> LedgerEntries { get; set; } = [];
}