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
    /// <summary>Mã cafe mục tiêu (nếu lobby cho 1 cafe cụ thể). Nullable.</summary>
    public Guid? CafeId { get; set; }

    /// <summary>Mã booking khi đã thanh toán cọc BR-05. Nullable.</summary>
    public Guid? BookingId { get; set; }

    // === Scheduling (BR-08) ===
    /// <summary>Thời điểm dự kiến bắt đầu chơi tại quán.</summary>
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
    /// Số ghế tối đa mà lobby này cần.
    /// BR-07: Members.Count <= SeatCount khi có giá trị.
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

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    public virtual User HostUser { get; set; } = null!;
    public virtual GameTemplate GameTemplate { get; set; } = null!;
    public virtual Cafe? Cafe { get; set; }
    public virtual BookingDeposit? Booking { get; set; }
    public virtual ActiveSession? ActiveSession { get; set; }
    public virtual ICollection<LobbyMember> Members { get; set; } = [];
    public virtual ICollection<LobbyInvite> Invites { get; set; } = [];
    public virtual ICollection<LobbyMessage> Messages { get; set; } = [];
}
