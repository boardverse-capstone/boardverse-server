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

    /// <summary>
    /// BR-22 (override BR-09): Trừ tiền cọc đã thanh toán vào tổng bill cuối.
    /// <para>
    /// <b>Default = true</b> (2026-10-03 — POS yêu cầu trừ cọc vào bill thay vì để BoardVerse giữ).
    /// Trước đây BR-09 quy định deposit là phí giữ chỗ, không cấn trừ; giờ thay đổi theo yêu cầu
    /// vận hành POS: tổng bill phải hiển thị đúng số tiền khách thực trả sau khi trừ deposit.
    /// </para>
    /// <para>
    /// Cách áp dụng:
    /// <list type="bullet">
    ///   <item><description><c>SplitByMember = false</c> (mặc định, cả bàn): cộng TẤT CẢ deposit
    ///     thuộc group (per-member) vào <c>session.DepositAppliedAmount</c>, trừ vào
    ///     <c>session.TotalAmount</c>. Mỗi member.DepositAppliedAmount = deposit tương ứng.</description></item>
    ///   <item><description><c>SplitByMember = true</c> (POS chọn "Chia tiền từng người"):
    ///     chỉ trừ deposit của HOST vào bill host; các member khác vẫn trả 100% tiền giờ.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Set <c>false</c> để giữ BR-09 cũ (deposit không trừ, BoardVerse giữ 100% làm phí giữ chỗ).
    /// </para>
    /// </summary>
    public bool DeductDepositFromBill { get; set; } = true;

    /// <summary>
    /// BR-22: Chế độ thanh toán per-member thay vì cả bàn.
    /// <para>
    /// Mặc định <c>false</c> (cả bàn). Khi <c>true</c>:
    /// <list type="bullet">
    ///   <item><description>Chỉ trừ deposit của HOST vào bill của host (per-member deposit khác bỏ qua).</description></item>
    ///   <item><description>Per-member invoice (MemberInvoiceDto) hiển thị DepositAppliedAmount cho host, các member khác = 0.</description></item>
    ///   <item><description>Session.TotalAmount vẫn = Subtotal + Penalty - HostDeposit (vì các thành viên tự trả phần của mình).</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public bool SplitByMember { get; set; } = false;
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

        // ============================================================
        // BR-22 (override BR-09): Trừ tiền cọc vào tổng bill
        // ============================================================

        /// <summary>
        /// BR-22: Tổng tiền cọc BookingDeposit (VND) đã cấn trừ vào tổng bill.
        /// <para>
        /// Mặc định = 0 (BR-09 cũ: deposit là phí giữ chỗ cho BoardVerse, không trừ vào bill).
        /// Khi <c>PaySessionRequestDto.DeductDepositFromBill = true</c>:
        /// <list type="bullet">
        ///   <item><description><c>SplitByMember = false</c> (cả bàn): cộng TẤT CẢ BookingDeposit
        ///     thuộc group (per-member flow BR-22) vào đây. <c>TotalAmount = Subtotal + Penalty - AppliedAmount</c>.</description></item>
        ///   <item><description><c>SplitByMember = true</c> (chia tiền): chỉ trừ deposit của HOST.
        ///     Per-member invoice hiển thị DepositAppliedAmount cho host = host deposit.</description></item>
        /// </list>
        /// </para>
        /// </summary>
        public decimal BookingDepositDeductedAmount { get; set; }

        /// <summary>
        /// BR-22: Danh sách các deposit đã áp dụng (để POS staff đối soát và hiển thị chi tiết).
        /// <para>Mỗi entry: 1 BookingDeposit (OrderId, Amount, UserId của người đặt).</para>
        /// </summary>
        public List<AppliedDepositInfo> AppliedDeposits { get; set; } = [];

        /// <summary>
        /// BR-22: Lý do bỏ qua deduction (nếu có). Null = áp dụng thành công (hoặc không có deposit).
        /// </summary>
        public string? DepositDeductionSkippedReason { get; set; }
    }

    /// <summary>
    /// BR-22: Thông tin 1 BookingDeposit đã được áp dụng để trừ vào bill.
    /// Trả trong <see cref="PaySessionResponseDto.AppliedDeposits"/>.
    /// </summary>
    public class AppliedDepositInfo
    {
        public Guid DepositId { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public decimal Amount { get; set; }

        /// <summary>Số tiền thực sự áp dụng (có thể &lt; Amount nếu bill thấp hơn deposit).</summary>
        public decimal AppliedAmount { get; set; }

        /// <summary>UserId của member nhận deduction (mặc định = UserId deposit).</summary>
        public Guid? AppliedToMemberId { get; set; }
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
