using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Reservation;

/// <summary>
/// Request t?o quote cho 1 reservation (?21A.2).
/// BR-DEPOSIT-01: Host tr? to?n b? c?c.
/// BR-NEW-15: timeSlot c? ??nh.
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

    /// <summary>Start time bắt buộc, nằm trong [timeSlot.startTime, timeSlot.endTime].</summary>
    [Required]
    public TimeOnly? PreferredStartTime { get; set; }

    /// <summary>End time bắt buộc, nằm trong [PreferredStartTime, timeSlot.endTime], cùng ngày playDate.</summary>
    [Required]
    public TimeOnly? PreferredEndTime { get; set; }

    /// <summary>
    /// 1-30 players. MinPlayers m?c ??nh 2 ?? ??m b?o lobby ?? ng??i.
    /// Solo play (MaxPlayers=1) ???c ph?p cho tr??ng h?p test ho?c ch?i m?t m?nh.
    /// </summary>
    [Range(1, 30)]
    public int MaxPlayers { get; set; }

    /// <summary>
    /// BR-LOBBY-02: ??t minPlayers l? ?? ?? x?c nh?n lobby.
    /// MinPlayers c? th? = 1 cho solo play, nh?ng m?c ??nh = 2.
    /// </summary>
    [Range(1, 30)]
    public int MinPlayers { get; set; } = 2;

    /// <summary>
    /// BR-NEW-11: Lobby ri?ng t? (m?i b?n) kh?ng c?n cafe duy?t.
    /// Public lobby m?i c?n duy?t n?u playDate > 2 ng?y.
    /// </summary>
    public bool IsPrivate { get; set; } = false;

    /// <summary>Idempotency key cho quote ? cho ph?p client retry.</summary>
    [Required, StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Quote tr? v? cho client (?21A.2 + BR ?XVIII.1).
/// Theo time-slot-fixed-end-design.md Section 10.1.
/// BR-BOOK-02: End time <= Start time + 6 hours.
/// BR-REFUND-05: Early checkout refund preview.
/// </summary>
public class ReservationQuoteDto
{
    public Guid? ReservationId { get; set; }

    public Guid CafeId { get; set; }
    public Guid GameId { get; set; }
    public DateOnly PlayDate { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public TimeOnly? PreferredStartTime { get; set; }
    public TimeOnly? PreferredEndTime { get; set; }

    /// <summary>
    /// BR-RESV-02: ScheduledStartTime + ScheduledEndTime luu DB luc <c>ConfirmAsync</c>.
    /// FE d?ng ?? hi?n th? th?i gian ??u-cu?i phi?n (?? qua ??m v?i slot <c>Night</c>).
    /// </summary>
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }
    public DateTime RecruitmentDeadline { get; set; }

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }

    /// <summary>
    /// Th?i l??ng slot (ph?t).
    /// BR-BOOK-02: Max 6 hours (360 minutes).
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>Lu?n = "BVC" ? 1 BVC = 1.000 VND (BR ?II.2).</summary>
    public string DepositUnit { get; set; } = "BVC";

    public long DepositRatePerPerson { get; set; }
    public long BaseDeposit { get; set; }
    public decimal RiskMultiplier { get; set; }
    public long MinDepositApplied { get; set; }
    public long FinalDeposit { get; set; }

    public long CurrentBalance { get; set; }
    public long MissingAmount { get; set; }

    /// <summary>
    /// Preview refund n?u early checkout (BR-REFUND-05).
    /// refund = 30% n?u playedRatio >= 50%, = 0% n?u playedRatio &lt; 50%.
    /// </summary>
    public EarlyCheckoutRefundPreview? EarlyCheckoutRefundPreview { get; set; }

    /// <summary>Buffer t? now ??n recruitmentDeadline (ph?t). ?m = qu? kh?.</summary>
    public int BufferMinutes { get; set; }

    /// <summary>True khi buffer &lt; 120 nh?ng ? 60 (c?nh b?o BR-LOBBY-01c).</summary>
    public bool BufferWarning { get; set; }

    /// <summary>True khi cafe c?n duy?t th? c?ng (BR-NEW-11).</summary>
    public bool RequiresCafeApproval { get; set; }

    /// <summary>Quote h?t h?n (BR ?XVIII.1 + 21A.2 ? 5 ph?t).</summary>
    public DateTime ExpiresAt { get; set; }

    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Preview refund cho early checkout.
/// Theo time-slot-fixed-end-design.md Section 3.4 - BR-REFUND-04/05.
/// </summary>
public class EarlyCheckoutRefundPreview
{
    /// <summary>
    /// T? l? th?i gian ch?i t?i thi?u ?? ???c refund 30%.
    /// BR-REFUND-05: >= 50% played = eligible for 30% refund.
    /// </summary>
    public double MinimumPlayedRatio { get; set; } = 0.5;

    /// <summary>
    /// T? l? refund khi ?? ?i?u ki?n.
    /// BR-REFUND-05: 30% refund n?u playedRatio >= 50%.
    /// BR-REFUND-06: 0% refund n?u playedRatio >= 90% (treated as on-time).
    /// </summary>
    public decimal RefundPercentage { get; set; } = 0.30m;

    /// <summary>
    /// S? BVC refund n?u early checkout ?? ?i?u ki?n.
    /// </summary>
    public long RefundAmount { get; set; }

    /// <summary>
    /// M? t? ch?nh s?ch refund.
    /// </summary>
    public string PolicyDescription { get; set; } = "Early checkout (>= 50% played): 30% refund";
}

/// <summary>
/// Request x?c nh?n reservation ? atomic hold BVC + seat + game copy (?21A.3).
/// Server t? t?nh l?i quote + t?o Reservation + Lobby trong 1 transaction.
/// IdempotencyKey ch?ng double-confirm (BR ?XVII.1).
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

    /// <summary>Start time bắt buộc, nằm trong [timeSlot.startTime, timeSlot.endTime].</summary>
    [Required]
    public TimeOnly? PreferredStartTime { get; set; }

    /// <summary>End time bắt buộc, nằm trong [PreferredStartTime, timeSlot.endTime], cùng ngày playDate.</summary>
    [Required]
    public TimeOnly? PreferredEndTime { get; set; }

    /// <summary>
    /// 1-30 players. MinPlayers m?c ??nh 2 ?? ??m b?o lobby ?? ng??i.
    /// Solo play (MaxPlayers=1) ???c ph?p cho tr??ng h?p test ho?c ch?i m?t m?nh.
    /// </summary>
    [Range(1, 30)]
    public int MaxPlayers { get; set; }

    /// <summary>
    /// BR-LOBBY-02: ??t minPlayers l? ?? ?? x?c nh?n lobby.
    /// MinPlayers c? th? = 1 cho solo play, nh?ng m?c ??nh = 2.
    /// </summary>
    [Range(1, 30)]
    public int MinPlayers { get; set; } = 2;

    /// <summary>
    /// BR-NEW-11: Lobby ri?ng t? (m?i b?n) kh?ng c?n cafe duy?t.
    /// </summary>
    public bool IsPrivate { get; set; } = false;

    /// <summary>
    /// Snapshot quote t? CreateQuoteAsync (BR ?XVIII.1).
    /// Server d?ng gi? tr? n?y ?? validate + ch?ng client g?i sai s? BVC.
    /// </summary>
    [Required]
    public long ExpectedFinalDeposit { get; set; }

    [Required, StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Response sau khi confirm th?nh c?ng ? tr? lobbyId ?? client navigate LobbyPage.
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
/// Host h?y lobby (?21A.6).
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
/// BR-REFUND-08 (?walk-in-override-design.md ?2.3):
/// Host h?y booking SAU khi ?? check-in t?i qu?n (late cancel).
/// ?p d?ng soft-release refund 30% n?u playedRatio ? 50% slot,
/// forfeit to?n b? n?u playedRatio &lt; 50%.
///
/// Trigger: <c>Reservation.Status: CheckedIn ? CancelledByPlayer</c>
/// (do player nh?n Cancel tr?n app, kh?ng ph?i POS staff).
///
/// Kh?c BR-REFUND-02 (cancel tr??c check-in): BR-REFUND-08 ch? ?p d?ng cho
/// session ?? check-in. N?u player cancel tr??c check-in ? BR-REFUND-02 (gi? nguy?n).
/// </summary>
public class CancelAfterCheckinRequestDto
{
    /// <summary>M? reservation c?n h?y sau check-in.</summary>
    [Required]
    public Guid ReservationId { get; set; }

    /// <summary>L? do h?y (optional, l?u audit log).</summary>
    [StringLength(500)]
    public string? Reason { get; set; }
}

/// <summary>
/// Response c?a BR-REFUND-08 endpoint.
/// </summary>
public class CancelAfterCheckinResponseDto
{
    public Guid ReservationId { get; set; }
    public Guid LobbyId { get; set; }
    public Guid? ActiveSessionId { get; set; }

    /// <summary>Minutes player ?? th?c s? ch?i (StartedAt ? now).</summary>
    public int PlayedMinutes { get; set; }

    /// <summary>Scheduled slot duration (ph?t). Reservation.ScheduledEndTime - ScheduledStartTime.</summary>
    public int ScheduledDurationMinutes { get; set; }

    /// <summary>playedRatio (0.0 - 1.0). L?m tr?n 2 ch? s? th?p ph?n.</summary>
    public decimal PlayedRatio { get; set; }

    /// <summary>S? BVC refund cho host (30% deposit n?u playedRatio ? 0.5, ng??c l?i 0).</summary>
    public long RefundBvc { get; set; }

    /// <summary>S? BVC forfeit v? doanh thu qu?n (70% deposit n?u playedRatio ? 0.5, 100% n?u &lt; 0.5).</summary>
    public long ForfeitBvc { get; set; }

    /// <summary>Policy ???c ?p d?ng: <c>"BR-REFUND-08 ? 0.5"</c> ho?c <c>"BR-REFUND-08 &lt; 0.5"</c>.</summary>
    public string RefundPolicyApplied { get; set; } = string.Empty;

    public DateTime CancelledAt { get; set; }
}

/// <summary>
/// Cafe duy?t/t? ch?i lobby pending (BR-NEW-11 ?XII).
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
/// POS scan QR check-in (?21A.7).
/// Manager/CafeStaff qu?t ReservationCode (8-char alphanumeric) hi?n th? tr?n BookingSuccessPage.
/// </summary>
public class ReservationCheckInRequestDto
{
    /// <summary>
    /// GAP #1 fix: CafeId c?a POS staff ?ang qu?t QR ? d?ng validate ownership trong CheckInAsync.
    /// Tr?nh staff cafe A scan QR reservation c?a cafe B.
    /// </summary>
    [Required]
    public Guid CafeId { get; set; }

    /// <summary>M? 8-char alphanumeric do Reservation.ReservationCode cung c?p.</summary>
    [Required, StringLength(16, MinimumLength = 4)]
    public string ReservationCode { get; set; } = string.Empty;

    /// <summary>Id c?a POS session g?n cho phi?n ch?i (FK ActiveSession).</summary>
    [Required]
    public Guid ActiveSessionId { get; set; }

    /// <summary>S? bàn staff gán cho nhóm. Null n?u ch?a gán.</summary>
    public int? TableNumber { get; set; }

    /// <summary>
    /// Idempotency key cho check-in (BR ?XVII.1) ? format g?i ?: "pos-checkin:{reservationCode}".
    /// GAP #6 fix: b? session.Id kh?i key ?? retry c?a c?ng POS attempt tr? c?ng response.
    /// </summary>
    [Required, StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Response sau check-in ? tr? ActiveSession ?? li?n k?t v?i Reservation.
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

    /// <summary>Số bàn được staff gán khi check-in. Null nếu chưa gán.</summary>
    public int? TableNumber { get; set; }
}

/// <summary>
/// Response tr? v? khi l?y chi ti?t 1 reservation.
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
    public TimeOnly? PreferredEndTime { get; set; }

    /// <summary>BR-RESV-02: scheduledStartTime + scheduledEndTime l?u DB.</summary>
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }

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

    /// <summary>BR-NEW-11: L? do cafe t? ch?i lobby (khi status = CancelledByCafe).</summary>
    public string? CafeRejectionReason { get; set; }

    public string ReservationCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>True n?u user hi?n t?i l? host c?a reservation n?y.</summary>
    public bool IsHost { get; set; }

    /// <summary>True n?u reservation ?ang ? tr?ng th?i cho ph?p h?y.</summary>
    public bool CanCancel { get; set; }

    // === 9 field m?i theo time-slot-fixed-end-design v2.0 ===

    /// <summary>Th?i ?i?m POS staff x?c nh?n check-in (UTC). Null n?u ch?a check-in.</summary>
    public DateTime? CheckedInAt { get; set; }

    /// <summary>Th?i ?i?m session th?c s? k?t th?c (UTC). Null n?u ch?a end.</summary>
    public DateTime? ActualEndAt { get; set; }

    /// <summary>T? l? th?i gian ?? ch?i (0.0 - 1.0). Null n?u ch?a end.</summary>
    public decimal? PlayedRatio { get; set; }

    /// <summary>L? do k?t th?c session (BR-END-*).</summary>
    public string? EndReason { get; set; }

    /// <summary>WalkInWindow ???c t?o khi early checkout / no-show (EC-09).</summary>
    public Guid? WalkInWindowId { get; set; }

    /// <summary>UserId ng??i ?? h?y reservation (host ho?c admin).</summary>
    public Guid? CancelledBy { get; set; }

    /// <summary>L? do h?y (host cancel, cafe cancel, no-show).</summary>
    public string? CancelReason { get; set; }

    /// <summary>Số bàn được staff gán khi check-in. Null nếu chưa check-in.</summary>
    public int? TableNumber { get; set; }
}

/// <summary>
/// Response tr? v? khi l?y danh s?ch reservation (list item).
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

    /// <summary>BR-RESV-02: scheduledStartTime + scheduledEndTime cho list item.</summary>
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }

    public DateTime RecruitmentDeadline { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsHost { get; set; }

    /// <summary>Số bàn được staff gán khi check-in. Null nếu chưa check-in.</summary>
    public int? TableNumber { get; set; }
}

/// <summary>
/// Request l?y danh s?ch reservation.
/// </summary>
public class ReservationListRequestDto
{
    /// <summary>Filter theo tr?ng th?i. Null = all non-terminal.</summary>
    public List<ReservationStatus>? Statuses { get; set; }

    /// <summary>Filter theo ng?y. Null = all.</summary>
    public DateOnly? PlayDate { get; set; }

    /// <summary>Filter theo cafe. Null = all.</summary>
    public Guid? CafeId { get; set; }

    /// <summary>Ch? l?y reservation do user host. Default true.</summary>
    public bool HostedByMe { get; set; } = true;

    /// <summary>Ch? l?y reservation user tham gia (member). Default false.</summary>
    public bool JoinedByMe { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Response paginated cho danh s?ch reservation.
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
/// Lobby pending cafe approval item cho dashboard c?a Manager (BR-NEW-11).
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
        TimeSlot.Morning => "Sáng (06:00 - 12:00)",
        TimeSlot.Afternoon => "Chiều (12:00 - 17:00)",
        TimeSlot.Evening => "Tối (17:00 - 23:00)",
        TimeSlot.LateNight => "Khuya (23:00 - 06:00)",
        _ => TimeSlot.ToString()
    };

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int CurrentPlayers { get; set; }

    public long DepositAmount { get; set; }

    /// <summary>BR-RESV-02: scheduledStartTime + scheduledEndTime cho manager dashboard.</summary>
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }

    public DateTime CafeApprovalDeadline { get; set; }
    public int RemainingApprovalHours { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request l?y danh s?ch lobby pending cafe approval.
/// </summary>
public class LobbyPendingApprovalRequestDto
{
    /// <summary>Filter theo cafe. Null = all cafes c?a manager.</summary>
    public Guid? CafeId { get; set; }

    /// <summary>Filter theo ng?y. Null = today.</summary>
    public DateOnly? PlayDate { get; set; }

    /// <summary>Filter theo tr?ng th?i lobby: Open, Viable, Full. Null = all.</summary>
    public List<LobbyStatus>? LobbyStatuses { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Response paginated cho danh s?ch lobby pending cafe approval.
/// </summary>
public class LobbyPendingApprovalListResponseDto
{
    public List<LobbyPendingApprovalItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
/// <summary>
/// BR-END-01..05 (?21A.8, ?3.4): POS end session + settle deposit.
/// </summary>
public class EndReservationRequestDto
{
    [Required]
    public Guid ReservationId { get; set; }

    /// <summary>Optional. Default = UTC now.</summary>
    public DateTime? ActualEndAt { get; set; }

    /// <summary>L? do end session (optional). VD: "staff_manual_close".</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>Optional: skip WalkInWindow creation khi playedRatio &lt; 50%. Default = false.</summary>
    public bool SkipWalkInWindow { get; set; }
}

/// <summary>
/// BR-END-01..05: Response after end session.
/// </summary>
public class EndReservationResponseDto
{
    public Guid ReservationId { get; set; }
    public SessionEndReason EndReason { get; set; }
    public decimal PlayedRatio { get; set; }
    public long OriginalDeposit { get; set; }
    public long RefundBvc { get; set; }
    public long ForfeitBvc { get; set; }
    public RefundReason RefundReason { get; set; }
    public DateTime CheckedInAt { get; set; }
    public DateTime ActualEndAt { get; set; }
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }

    /// <summary>WalkInWindow du?c t?o n?u playedRatio &lt; 50% (EC-09).</summary>
    public Guid? WalkInWindowId { get; set; }

    /// <summary>True n?u player b? tr? Karma (Phase 7).</summary>
    public bool KarmaRecorded { get; set; }
}
