using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BoardVerse.Core.DTOs.WalkIn;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Session
{
/// <summary>
/// Request thanh toán hóa đơn tổng của phiên chơi.
/// BR-15: TotalAmount = Subtotal + Penalty - DepositAppliedAmount
/// M1 (BR-15 modified): TotalAmount = Subtotal + Penalty - DepositAppliedAmount - DiscountAmount
///                       (host chọn dùng deposit làm discount cho members)
/// </summary>
public class PaySessionRequestDto
{
    /// <summary>
    /// Danh sách linh kiện bị mất/hỏng và mức phạt.
    /// <para>
    /// <b>[DEPRECATED]</b> từ 2026-08. Penalty giờ là <i>single source of truth</i>
    /// từ <c>ComponentCheckResult.ResponsibleMemberId</c> (submit lúc component-check).
    /// Endpoint vẫn nhận field này cho back-compat với POS client cũ; sẽ log warning
    /// và cộng dồn vào <c>session.PenaltyAmount</c>, nhưng KHÔNG ảnh hưởng per-member invoice.
    /// </para>
    /// </summary>
    [Obsolete("Dùng ResponsibleMemberId trong ComponentCheckResultItemDto lúc submit component-check.")]
    public List<ComponentPenaltyItemDto>? PenaltyItems { get; set; }

    /// <summary>Ghi chú thanh toán (optional).</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// M1 / BR-15 modified: Cách host muốn sử dụng deposit của mình khi thanh toán.
    /// Default: <see cref="HostDepositUsageMode.None"/> (giữ BR-09 cũ — capture 100% deposit).
    /// POS staff chọn dropdown lúc Pay (M1 Phase 5 — Frontend minimal).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HostDepositUsageMode HostDepositUsage { get; set; } = HostDepositUsageMode.None;

    /// <summary>
    /// M1 / Idempotency key cho discount flow.
    /// Format gợi ý: <c>pay-session-discount-{SessionId}-{HostDepositUsage}-{Timestamp}</c>.
    /// Nếu null/empty → service tự sinh từ sessionId + HostDepositUsage.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Item phạt cho linh kiện bị mất/hỏng.
/// BR-14: Không gán phí phạt cho Guest_Slot.
/// </summary>
public class ComponentPenaltyItemDto
{
    [Required]
    public Guid ComponentId { get; set; }

    [Required]
    public string ComponentName { get; set; } = string.Empty;

    [Required]
    public decimal PenaltyAmount { get; set; }

    /// <summary>Mã thành viên chịu trách nhiệm (nếu có). Không áp dụng cho Guest_Slot (BR-14).</summary>
    public Guid? ResponsibleMemberId { get; set; }
}

    /// <summary>
    /// Response sau khi thanh toán hóa đơn tổng.
    /// BR-15: TotalAmount = Subtotal + PenaltyAmount - DepositAppliedAmount
    /// GAP-33 Fix: Thêm danh sách hóa đơn per-member
    /// GAP-34 Fix: Thêm thông tin BVC capture status
    /// §4.4: Thêm WalkInWindow nếu early checkout
    /// M1 (BR-15 modified): Thêm thông tin deposit discount distribution cho Case 1.
    /// </summary>
    public class PaySessionResponseDto
    {
        public Guid SessionId { get; set; }
        public decimal Subtotal { get; set; }
        public decimal PenaltyAmount { get; set; }
        public decimal DepositAppliedAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime PaidAt { get; set; }

        /// <summary>Danh sách hóa đơn cá nhân của từng thành viên.</summary>
        public List<MemberInvoiceDto> MemberInvoices { get; set; } = [];

        /// <summary>Trạng thái capture BVC của toàn phiên.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BvcCaptureStatus BvcCaptureStatus { get; set; }

        /// <summary>
        /// §4.4: WalkInWindow được tạo nếu early checkout (về sớm trước ScheduledEndTime).
        /// POS staff dùng thông tin này để hướng dẫn walk-in.
        /// </summary>
        public BoardVerse.Core.DTOs.WalkIn.WalkInWindowDto? WalkInWindow { get; set; }

        public ActiveSessionResponseDto Session { get; set; } = null!;

        // ============================================================
        // M1 / Host Deposit Discount (BR-15 modified)
        // docs/design/host-deposit-discount-and-bvc-payment-design.md §B2.6
        // ============================================================

        /// <summary>
        /// M1: Tổng BVC đã áp dụng làm discount cho members.
        /// = 0 nếu <see cref="HostDepositUsage"/> = None hoặc session không có reservation/deposit.
        /// Bằng tổng <c>MemberInvoiceDto.DiscountAppliedAmount</c> của các member.
        /// </summary>
        public long HostDepositDiscountApplied { get; set; }

        /// <summary>
        /// M1: Breakdown chi tiết deposit discount phân bổ cho từng member.
        /// Empty nếu <see cref="HostDepositUsage"/> = None.
        /// </summary>
        public List<DepositAppliedBreakdown> DepositAppliedBreakdown { get; set; } = [];

        /// <summary>
        /// M1: Lobby status tại thời điểm Pay (audit trail).
        /// Null nếu session không liên kết lobby (walk-in).
        /// </summary>
        public string? LobbyStatusAtPay { get; set; }

        /// <summary>
        /// M1: Reservation status tại thời điểm Pay (audit trail).
        /// Null nếu session không liên kết reservation.
        /// </summary>
        public string? ReservationStatusAtPay { get; set; }

        /// <summary>
        /// M1: Lý do bỏ qua discount (nếu có).
        /// Null = discount applied thành công (hoặc không có deposit để apply).
        /// Value có thể là: "LobbyTerminal", "ReservationCancelled", "NoActiveMembers", "DepositAlreadyCaptured".
        /// </summary>
        public string? DiscountSkippedReason { get; set; }
    }

    /// <summary>
    /// M1: Breakdown BVC discount phân bổ cho từng member.
    /// Trả trong <see cref="PaySessionResponseDto.DepositAppliedBreakdown"/>.
    /// </summary>
    public class DepositAppliedBreakdown
    {
        public Guid MemberId { get; set; }
        public Guid? UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsHost { get; set; }
        public bool IsGuestSlot { get; set; }
        public int MinutesPlayed { get; set; }

        /// <summary>BVC discount đã apply cho member này.</summary>
        public long DiscountAppliedBvc { get; set; }
    }
}
