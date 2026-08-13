using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Phòng chờ trực tuyến (Lobby).
/// Theo boardverse-state-machine.mdc - Section 2.
/// BR-07: Lobby.MaxMembers <= SeatCount (tự quản lý)
/// BR-08: Auto-hủy nếu trước giờ hẹn X phút mà chưa đạt MinPlayers
/// BR-10: Filter theo Karma (không dùng Elo)
/// </summary>
public class Lobby
{
    public Guid Id { get; set; }

    // === Game & Host ===
    public Guid HostUserId { get; set; }
    public Guid GameTemplateId { get; set; }

    // === Optional links ===
    /// <summary>Mã cafe mục tiêu (nếu lobby cho 1 cafe cụ thể). Nullable.
    /// §9.7: MIRROR — source of truth là <c>Reservation.CafeId</c>. Giữ cho backward compat
    /// (legacy lobby không có Reservation) và index query nhanh (IX_Lobbies_PlayDate).
    /// [Obsolete("§9.7: Query từ Reservation.CafeId thay vì Lobby.CafeId — sẽ drop ở Phase 4.")]
    public Guid? CafeId { get; set; }

    /// <summary>Mã booking khi đã thanh toán cọc BR-05. Nullable.
    /// §9.7: Legacy FK — không dùng cho Reservation flow mới (dùng <c>ReservationId</c>).
    /// [Obsolete("§9.7: Legacy Booking flow không dùng cho Reservation. Sẽ drop ở Phase 4.")]
    public Guid? BookingId { get; set; }

    // ===== BR-NEW-* §19.1: Bổ sung cho Reservation flow =====
    /// <summary>FK Reservation — bắt buộc với lobby mới. Nullable để tương thích lobby cũ.</summary>
    public Guid? ReservationId { get; set; }

    /// <summary>BR-NEW-04: ngày dự kiến chơi (chỉ ngày, không giờ).
    /// §9.7: MIRROR từ <c>Reservation.PlayDate</c>. Giữ cho index IX_Lobbies_PlayDate.
    /// [Obsolete("§9.7: Query từ Reservation.PlayDate thay vì Lobby.PlayDate.")]
    public DateOnly? PlayDate { get; set; }

    /// <summary>BR-NEW-15: khung giờ cố định.
    /// §9.7: MIRROR từ <c>Reservation.TimeSlot</c>. Sẽ drop ở Phase 2+ khi không còn legacy lobby.
    /// [Obsolete("§9.7: Derive từ Reservation.TimeSlot — sẽ drop ở Phase 2+.")]
    public TimeSlot? TimeSlot { get; set; }

    /// <summary>BR-NEW-15b: optional, nằm trong [timeSlot.startTime, timeSlot.endTime].
    /// §9.7: MIRROR từ <c>Reservation.PreferredStartTime</c>. Sẽ drop ở Phase 2+.
    /// [Obsolete("§9.7: Derive từ Reservation.PreferredStartTime — sẽ drop ở Phase 2+.")]
    public TimeOnly? PreferredStartTime { get; set; }

    /// <summary>§9.7: MIRROR từ <c>Reservation.PreferredEndTime</c>. Sẽ drop ở Phase 2+.
    /// [Obsolete("§9.7: Derive từ Reservation.PreferredEndTime — sẽ drop ở Phase 2+.")]
    public TimeOnly? PreferredEndTime { get; set; }

    /// <summary>BR-LOBBY-01: scheduledTime - leadTimeMinutes.
    /// §9.7: MIRROR từ <c>Reservation.RecruitmentDeadline</c>. Giữ cho index IX_Lobbies_RecruitmentDeadline.
    /// [Obsolete("§9.7: Query từ Reservation.RecruitmentDeadline thay vì Lobby.RecruitmentDeadline.")]
    public DateTime? RecruitmentDeadline { get; set; }

    /// <summary>BVC minDeposit theo khoảng cách playDate (BR-NEW-01 §8).
    /// §9.7: MIRROR từ <c>Reservation.DepositAmount</c>. Sẽ drop ở Phase 2+.
    /// [Obsolete("§9.7: Derive từ Reservation.DepositAmount — sẽ drop ở Phase 2+.")]
    public long? MinDeposit { get; set; }

    /// <summary>Snapshot cấu hình cọc tại thời điểm tạo (§19.1 + 21F.9).
    /// §9.7: MIRROR từ <c>Reservation.DepositConfigSnapshot</c>. Sẽ drop ở Phase 2+.
    /// [Obsolete("§9.7: Derive từ Reservation.DepositConfigSnapshot — sẽ drop ở Phase 2+.")]
    public DepositSnapshot? DepositSnapshot { get; set; }

    // ===== BR-NEW-11 §XII: cafe approval workflow =====
    public DateTime? CafeApprovalDeadline { get; set; }
    public Guid? CafeApprovedByUserId { get; set; }
    public DateTime? CafeApprovedAt { get; set; }
    public string? CafeRejectionReason { get; set; }

    // === Scheduling (BR-08) ===
    /// <summary>Thời điểm dự kiến bắt đầu chơi tại quán.
    /// §9.7: MIRROR từ <c>Reservation.ScheduledStartTime</c>. Giữ cho index IX_Lobbies_ScheduledStartTime.
    /// Query trực tiếp từ <c>Reservation.ScheduledStartTime</c> khi cần độ chính xác cao.
    /// </summary>
    public DateTime? ScheduledStartTime { get; set; }

    /// <summary>Latitude của quán mục tiêu (từ Cafe) - dùng để tìm phòng chờ gần user.</summary>
    public double? Latitude { get; set; }

    /// <summary>Longitude của quán mục tiêu (từ Cafe) - dùng để tìm phòng chờ gần user.</summary>
    public double? Longitude { get; set; }

    /// <summary>Phút trước giờ hẹn để trigger auto-hủy nếu chưa đủ người. (BR-08)</summary>
    public int CancellationLeadTimeMinutes { get; set; } = 30;

    // === Capacity (BR-07) ===
    /// <summary>Số người tối đa trong phòng chờ. Phải nằm trong [GameTemplate.MinPlayers, GameTemplate.MaxPlayers].</summary>
    public int MaxMembers { get; set; }

    /// <summary>Số người tối thiểu để có thể Lock/Start. Mặc định = 2.</summary>
    public int MinPlayers { get; set; } = 2;

    /// <summary>
    /// BR-10: Karma tối thiểu của member để có thể join lobby (optional).
    /// Null = không yêu cầu tối thiểu. Validate khi member join.
    /// </summary>
    public int? MinKarmaScore { get; set; }

    /// <summary>
    /// Số ghế tối đa mà lobby này cần.
    /// BR-07: Members.Count &lt;= SeatCount khi có giá trị.
    /// </summary>
    public int? SeatCount { get; set; }

    // === Visibility / Invite ===
    /// <summary>
    /// Lobby công khai hay riêng tư.
    /// - false (public): mọi user tìm qua /search đều có thể join; host cũng có thể gửi invite cho bạn bè.
    /// - true (private): chỉ join được qua invite (LobbyInvite hoặc ShareCode); không xuất hiện trong search.
    /// </summary>
    public bool IsPrivate { get; set; } = false;

    /// <summary>
    /// Mã share ngắn (8 ký tự, alphanumeric uppercase) để mời nhanh qua link.
    /// Sinh tự động khi tạo lobby; unique trong hệ thống.
    /// </summary>
    public string ShareCode { get; set; } = string.Empty;

    // === Display ===
    /// <summary>Mô tả ngắn do Host nhập (vd: "Catan thường, Cường + 2 bạn").</summary>
    public string? Description { get; set; }

    /// <summary>URL ảnh bìa lobby (optional).</summary>
    public string? CoverImageUrl { get; set; }

    // === Session link ===
    public Guid? ActiveSessionId { get; set; }

    // === State ===
    public LobbyStatus Status { get; set; } = LobbyStatus.Open;

    /// <summary>Thời điểm mở màn hình đánh giá Karma.</summary>
    public DateTime? RatingOpenedAt { get; set; }

    /// <summary>Thời điểm đóng lobby (Closed/TimeoutFailed/HostCancelled).</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>Lý do đóng (audit trail).</summary>
    public string? ClosedReason { get; set; }

    /// <summary>
    /// BR-LOBBY-READY-03: Mốc thời điểm lobby chuyển sang FULL.
    /// Scheduler dùng để timeout 20p nếu không có ai Ready.
    /// Reset về null khi lobby rời Full (vd: host khóa sớm, member rời khiến ActiveMembers &lt; MaxMembers).
    /// </summary>
    public DateTime? FullAt { get; set; }

    /// <summary>
    /// BR-READY-TIMEOUT-MINUTES: Số phút cho phép sau khi lobby FULL mà không có ai Ready trước khi timeout.
    /// Mặc định 20 phút — có thể cấu hình sau.
    /// </summary>
    public const int ReadyTimeoutMinutes = 20;

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    public virtual User HostUser { get; set; } = null!;
    public virtual GameTemplate GameTemplate { get; set; } = null!;
    public virtual Cafe? Cafe { get; set; }
    public virtual BookingDeposit? Booking { get; set; }
    public virtual Reservation? Reservation { get; set; }
    public virtual ActiveSession? ActiveSession { get; set; }
    public virtual ICollection<LobbyMember> Members { get; set; } = [];
    public virtual ICollection<LobbyInvite> Invites { get; set; } = [];
    public virtual ICollection<LobbyMessage> Messages { get; set; } = [];

    // N-01: Lobby milestone notification tracking (BR-NEW-13)
    public virtual ICollection<LobbyNotificationSent> NotificationSents { get; set; } = [];

    // N-02: Lobby at-risk warning tracking (BR-NEW-14)
    public virtual ICollection<LobbyAtRiskWarning> AtRiskWarnings { get; set; } = [];
}
