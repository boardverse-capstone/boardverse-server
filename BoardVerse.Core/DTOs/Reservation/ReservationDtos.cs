using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Reservation;

/// <summary>
/// Request tạo quote cho 1 reservation (§21A.2).
/// BR-DEPOSIT-01: Host trả toàn bộ cọc.
/// BR-NEW-15: timeSlot cố định.
/// </summary>
public class ReservationQuoteRequestDto
{
    [Required]
    public Guid CafeId { get; set; }

    [Required]
    public Guid GameId { get; set; }

    [Required]
    public DateOnly PlayDate { get; set; }

    [Required]
    public TimeSlot TimeSlot { get; set; }

    /// <summary>Optional, phải nằm trong [timeSlot.startTime, timeSlot.endTime].</summary>
    public TimeOnly? PreferredStartTime { get; set; }

    [Range(2, 30)]
    public int MaxPlayers { get; set; }

    [Range(2, 30)]
    public int MinPlayers { get; set; } = 2;

    /// <summary>
    /// BR-NEW-11: Lobby riêng tư (mời bạn) không cần cafe duyệt.
    /// Public lobby mới cần duyệt nếu playDate > 2 ngày.
    /// </summary>
    public bool IsPrivate { get; set; } = false;

    /// <summary>Idempotency key cho quote — cho phép client retry.</summary>
    [Required, StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Quote trả về cho client (§21A.2 + BR §XVIII.1).
/// </summary>
public class ReservationQuoteDto
{
    public Guid? ReservationId { get; set; }

    public Guid CafeId { get; set; }
    public Guid GameId { get; set; }
    public DateOnly PlayDate { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public TimeOnly? PreferredStartTime { get; set; }

    public DateTime ScheduledTime { get; set; }
    public DateTime RecruitmentDeadline { get; set; }

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }

    /// <summary>Luôn = "BVC" — 1 BVC = 1.000 VND (BR §II.2).</summary>
    public string DepositUnit { get; set; } = "BVC";

    public long DepositRatePerPerson { get; set; }
    public long BaseDeposit { get; set; }
    public decimal RiskMultiplier { get; set; }
    public long MinDepositApplied { get; set; }
    public long FinalDeposit { get; set; }

    public long CurrentBalance { get; set; }
    public long MissingAmount { get; set; }

    /// <summary>Buffer từ now đến recruitmentDeadline (phút). Âm = quá khứ.</summary>
    public int BufferMinutes { get; set; }

    /// <summary>True khi buffer &lt; 120 nhưng ≥ 60 (cảnh báo BR-LOBBY-01c).</summary>
    public bool BufferWarning { get; set; }

    /// <summary>True khi cafe cần duyệt thủ công (BR-NEW-11).</summary>
    public bool RequiresCafeApproval { get; set; }

    /// <summary>Quote hết hạn (BR §XVIII.1 + 21A.2 — 5 phút).</summary>
    public DateTime ExpiresAt { get; set; }

    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Request xác nhận reservation — atomic hold BVC + seat + game copy (§21A.3).
/// Server tự tính lại quote + tạo Reservation + Lobby trong 1 transaction.
/// IdempotencyKey chống double-confirm (BR §XVII.1).
/// </summary>
public class ReservationConfirmRequestDto
{
    [Required]
    public Guid CafeId { get; set; }

    [Required]
    public Guid GameId { get; set; }

    [Required]
    public DateOnly PlayDate { get; set; }

    [Required]
    public TimeSlot TimeSlot { get; set; }

    /// <summary>Optional, phải nằm trong [timeSlot.startTime, timeSlot.endTime].</summary>
    public TimeOnly? PreferredStartTime { get; set; }

    [Range(2, 30)]
    public int MaxPlayers { get; set; }

    [Range(2, 30)]
    public int MinPlayers { get; set; } = 2;

    /// <summary>
    /// BR-NEW-11: Lobby riêng tư (mời bạn) không cần cafe duyệt.
    /// </summary>
    public bool IsPrivate { get; set; } = false;

    /// <summary>
    /// Snapshot quote từ CreateQuoteAsync (BR §XVIII.1).
    /// Server dùng giá trị này để validate + chống client gửi sai số BVC.
    /// </summary>
    [Required]
    public long ExpectedFinalDeposit { get; set; }

    [Required, StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Response sau khi confirm thành công — trả lobbyId để client navigate LobbyPage.
/// </summary>
public class ReservationConfirmResponseDto
{
    public Guid ReservationId { get; set; }
    public Guid LobbyId { get; set; }
    public DateTime RecruitmentDeadline { get; set; }
    public bool RequiresCafeApproval { get; set; }
    public DateTime? CafeApprovalDeadline { get; set; }
    public long HeldBvc { get; set; }
}

/// <summary>
/// Host hủy lobby (§21A.6).
/// </summary>
public class CancelReservationRequestDto
{
    [Required]
    public Guid ReservationId { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}

public class CancelReservationResponseDto
{
    public Guid ReservationId { get; set; }
    public Guid LobbyId { get; set; }
    public long RefundBvc { get; set; }
    public long ForfeitBvc { get; set; }
    public string RefundPolicyApplied { get; set; } = string.Empty;
}

/// <summary>
/// Cafe duyệt/từ chối lobby pending (BR-NEW-11 §XII).
/// </summary>
public class CafeApprovalRequestDto
{
    [Required]
    public Guid ReservationId { get; set; }

    public bool Approve { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}

public class CafeApprovalResponseDto
{
    public Guid ReservationId { get; set; }
    public Guid LobbyId { get; set; }
    public string LobbyStatus { get; set; } = string.Empty;
    public bool Approved { get; set; }
    public long RefundBvc { get; set; }
}

/// <summary>
/// POS scan QR check-in (§21A.7).
/// Manager/CafeStaff quét ReservationCode (8-char alphanumeric) hiển thị trên BookingSuccessPage.
/// </summary>
public class ReservationCheckInRequestDto
{
    /// <summary>
    /// GAP #1 fix: CafeId của POS staff đang quét QR — dùng validate ownership trong CheckInAsync.
    /// Tránh staff cafe A scan QR reservation của cafe B.
    /// </summary>
    [Required]
    public Guid CafeId { get; set; }

    /// <summary>Mã 8-char alphanumeric do Reservation.ReservationCode cung cấp.</summary>
    [Required, StringLength(16, MinimumLength = 4)]
    public string ReservationCode { get; set; } = string.Empty;

    /// <summary>Id của POS session gán cho phiên chơi (FK ActiveSession).</summary>
    [Required]
    public Guid ActiveSessionId { get; set; }

    /// <summary>
    /// Idempotency key cho check-in (BR §XVII.1) — format gợi ý: "pos-checkin:{reservationCode}".
    /// GAP #6 fix: bỏ session.Id khỏi key để retry của cùng POS attempt trả cùng response.
    /// </summary>
    [Required, StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Response sau check-in — trả ActiveSession đã liên kết với Reservation.
/// </summary>
public class ReservationCheckInResponseDto
{
    public Guid ReservationId { get; set; }
    public Guid LobbyId { get; set; }
    public Guid ActiveSessionId { get; set; }
    public string ReservationStatus { get; set; } = string.Empty;
    public string LobbyStatus { get; set; } = string.Empty;
    public DateTime CheckedInAt { get; set; }
    public long HeldBvc { get; set; }
}

/// <summary>
/// Response trả về khi lấy chi tiết 1 reservation.
/// </summary>
public class ReservationDetailDto
{
    public Guid Id { get; set; }
    public Guid HostId { get; set; }
    public string HostName { get; set; } = string.Empty;

    public Guid CafeId { get; set; }
    public string CafeName { get; set; } = string.Empty;
    public string CafeAddress { get; set; } = string.Empty;

    public Guid GameId { get; set; }
    public string GameName { get; set; } = string.Empty;

    public DateOnly PlayDate { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public TimeOnly? PreferredStartTime { get; set; }

    public DateTime ScheduledTime { get; set; }
    public DateTime RecruitmentDeadline { get; set; }

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int CurrentPlayers { get; set; }

    public string Status { get; set; } = string.Empty;

    public long DepositAmount { get; set; }
    public decimal RiskMultiplier { get; set; }
    public string RefundPolicyApplied { get; set; } = string.Empty;

    public Guid? LobbyId { get; set; }
    public string? LobbyShareCode { get; set; }
    public string? LobbyStatus { get; set; }

    /// <summary>BR-NEW-11: Lý do cafe từ chối lobby (khi status = CancelledByCafe).</summary>
    public string? CafeRejectionReason { get; set; }

    public string ReservationCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>True nếu user hiện tại là host của reservation này.</summary>
    public bool IsHost { get; set; }

    /// <summary>True nếu reservation đang ở trạng thái cho phép hủy.</summary>
    public bool CanCancel { get; set; }
}

/// <summary>
/// Response trả về khi lấy danh sách reservation (list item).
/// </summary>
public class ReservationListItemDto
{
    public Guid Id { get; set; }

    public Guid CafeId { get; set; }
    public string CafeName { get; set; } = string.Empty;

    public Guid GameId { get; set; }
    public string GameName { get; set; } = string.Empty;

    public DateOnly PlayDate { get; set; }
    public TimeSlot TimeSlot { get; set; }

    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }

    public string Status { get; set; } = string.Empty;

    public long DepositAmount { get; set; }

    public Guid? LobbyId { get; set; }
    public string? LobbyStatus { get; set; }

    public string ReservationCode { get; set; } = string.Empty;

    public DateTime RecruitmentDeadline { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsHost { get; set; }
}

/// <summary>
/// Request lấy danh sách reservation.
/// </summary>
public class ReservationListRequestDto
{
    /// <summary>Filter theo trạng thái. Null = all non-terminal.</summary>
    public List<ReservationStatus>? Statuses { get; set; }

    /// <summary>Filter theo ngày. Null = all.</summary>
    public DateOnly? PlayDate { get; set; }

    /// <summary>Filter theo cafe. Null = all.</summary>
    public Guid? CafeId { get; set; }

    /// <summary>Chỉ lấy reservation do user host. Default true.</summary>
    public bool HostedByMe { get; set; } = true;

    /// <summary>Chỉ lấy reservation user tham gia (member). Default false.</summary>
    public bool JoinedByMe { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Response paginated cho danh sách reservation.
/// </summary>
public class ReservationListResponseDto
{
    public List<ReservationListItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// Lobby pending cafe approval item cho dashboard của Manager (BR-NEW-11).
/// </summary>
public class LobbyPendingApprovalItemDto
{
    public Guid ReservationId { get; set; }
    public Guid LobbyId { get; set; }

    public Guid HostId { get; set; }
    public string HostName { get; set; } = string.Empty;

    public Guid CafeId { get; set; }
    public string CafeName { get; set; } = string.Empty;

    public Guid GameId { get; set; }
    public string GameName { get; set; } = string.Empty;

    public DateOnly PlayDate { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public string TimeSlotDisplay => TimeSlot switch
    {
        TimeSlot.Morning => "Sáng (09:00 - 13:00)",
        TimeSlot.Afternoon => "Chiều (13:00 - 18:00)",
        TimeSlot.Evening => "Tối (18:00 - 23:00)",
        TimeSlot.Night => "Khuya (19:00 - 24:00)",
        _ => TimeSlot.ToString()
    };

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int CurrentPlayers { get; set; }

    public long DepositAmount { get; set; }

    public DateTime ScheduledTime { get; set; }
    public DateTime CafeApprovalDeadline { get; set; }
    public int RemainingApprovalHours { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request lấy danh sách lobby pending cafe approval.
/// </summary>
public class LobbyPendingApprovalRequestDto
{
    /// <summary>Filter theo cafe. Null = all cafes của manager.</summary>
    public Guid? CafeId { get; set; }

    /// <summary>Filter theo ngày. Null = today.</summary>
    public DateOnly? PlayDate { get; set; }

    /// <summary>Filter theo trạng thái lobby: Open, Viable, Full. Null = all.</summary>
    public List<LobbyStatus>? LobbyStatuses { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Response paginated cho danh sách lobby pending cafe approval.
/// </summary>
public class LobbyPendingApprovalListResponseDto
{
    public List<LobbyPendingApprovalItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}