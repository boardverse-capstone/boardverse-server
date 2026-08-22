using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Response DTO cho Player xem phiên chơi hiện tại của mình.
    /// GET /api/v1/sessions/me/current
    /// </summary>
    public class GetCurrentSessionResponseDto
    {
        public Guid SessionId { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public Guid CafeId { get; set; }

        // GAP-12 Fix: LobbyId để client phân biệt lobby session vs walk-in session
        public Guid? LobbyId { get; set; }

        /// <summary>Trạng thái của thành viên trong phiên: Playing/SuspendedMutation/Finished</summary>
        public IndividualSessionStatus MemberStatus { get; set; }

        /// <summary>Trạng thái của phiên nhóm: Active/Checking/Unpaid/Paid</summary>
        public GroupSessionStatus SessionStatus { get; set; }

        public DateTime JoinedAt { get; set; }

        /// <summary>
        /// GAP-3 Fix: JoinedAt kèm timezone offset (UTC+7 cho VN).
        /// FE dùng `.toLocal()` để convert sang timezone của user.
        /// </summary>
        public DateTimeOffset JoinedAtOffset { get; set; }

        public int ElapsedMinutes { get; set; }
        public int TotalMinutesPlayed { get; set; }

        /// <summary>Ước tính chi phí hiện tại của player.</summary>
        public PlayerCostEstimateDto CostEstimate { get; set; } = new();

        public string GameName { get; set; } = string.Empty;
        public int TotalGroupMembers { get; set; }

        /// <summary>Player có thể gia hạn thêm thời gian không.</summary>
        public bool CanExtend { get; set; }

        /// <summary>Player có thể thanh toán ngay không (phiên đang ở trạng thái Unpaid).</summary>
        public bool CanPay { get; set; }

        /// <summary>
        /// GAP-9 Fix: Phiên đã thanh toán xong chưa.
        /// Nếu true, hiển thị "Đã thanh toán" thay vì ẩn phiên.
        /// </summary>
        public bool IsPaid { get; set; }

        /// <summary>
        /// GAP-9 Fix: Yêu cầu gia hạn gần nhất của player (Pending/Approved/Rejected/Expired).
        /// Null nếu player chưa từng yêu cầu gia hạn.
        /// </summary>
        public LastExtensionRequestDto? LastExtensionRequest { get; set; }
    }

    /// <summary>
    /// GAP-9 Fix: Yêu cầu gia hạn gần nhất của player trong session hiện tại.
    /// </summary>
    public class LastExtensionRequestDto
    {
        public Guid RequestId { get; set; }
        public int RequestedMinutes { get; set; }
        public int? ApprovedMinutes { get; set; }
        public decimal EstimatedAdditionalCostVnd { get; set; }
        public string Status { get; set; } = string.Empty; // Pending/Approved/Rejected/Expired
        public string? RejectionReason { get; set; }
        public string RequestedAt { get; set; } = string.Empty;
        public DateTime RequestedAtUtc { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTimeOffset? ProcessedAtOffset { get; set; }
    }

    /// <summary>
    /// Ước tính chi phí cá nhân của player.
    /// </summary>
    public class PlayerCostEstimateDto
    {
        public int BaseMinutes { get; set; }
        public decimal Subtotal { get; set; }
        public decimal PenaltyAmount { get; set; }
        public decimal DepositApplied { get; set; }
        public decimal TotalDue { get; set; }
        public string Currency { get; set; } = "VND";
    }

    /// <summary>
    /// Request DTO để player gia hạn thêm thời gian chơi.
    /// POST /api/v1/sessions/me/extend
    /// </summary>
    public class ExtendSessionRequestDto
    {
        /// <summary>Số phút muốn gia hạn thêm.</summary>
        public int ExtensionMinutes { get; set; }
    }

    /// <summary>
    /// Response DTO sau khi gia hạn thành công.
    /// </summary>
    public class ExtendSessionResponseDto
    {
        // GAP-16 Fix: Thêm fields để trả về khi duplicate pending request
        public Guid RequestId { get; set; }
        public Guid SessionId { get; set; }
        public int RequestedMinutes { get; set; }
        public decimal EstimatedAdditionalCostVnd { get; set; }
        public string Status { get; set; } = string.Empty;

        public bool Success { get; set; }
        public string? Message { get; set; }
        public DateTime? NewEndTime { get; set; }
        public int TotalMinutesBooked { get; set; }
        public decimal EstimatedAdditionalCost { get; set; }
    }

    /// <summary>
    /// Response DTO sau khi thanh toán bằng BVC.
    /// POST /api/v1/sessions/me/pay
    /// </summary>
    public class PlayerPaySessionResponseDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }

        /// <summary>Hóa đơn cá nhân của player.</summary>
        public PlayerInvoiceDto Invoice { get; set; } = new();

        /// <summary>Số BVC đã trừ.</summary>
        public long BvcDeducted { get; set; }

        /// <summary>Số dư BVC còn lại sau thanh toán.</summary>
        public long RemainingBvcBalance { get; set; }

        /// <summary>PHƯƠNG THỨC THANH TOÁN: "BVC" | "CASH" | "QR"</summary>
        public string PaymentMethod { get; set; } = "BVC";
    }

    /// <summary>
    /// Request DTO để player thanh toán phiên chơi bằng BVC.
    /// POST /api/v1/sessions/me/pay
    /// </summary>
    public class PlayerPaySessionRequestDto
    {
        /// <summary>Mã phiên chơi cần thanh toán.</summary>
        public Guid SessionId { get; set; }
    }

    /// <summary>
    /// Hóa đơn cá nhân của player sau khi thanh toán.
    /// </summary>
    public class PlayerInvoiceDto
    {
        public Guid SessionId { get; set; }
        public int TotalMinutes { get; set; }
        public decimal Subtotal { get; set; }
        public decimal PenaltyAmount { get; set; }
        public decimal DepositApplied { get; set; }
        public decimal TotalDue { get; set; }
        public string Currency { get; set; } = "VND";

        /// <summary>
        /// GAP-11 Fix: Breakdown chi tiết từng khoản phí.
        /// </summary>
        public List<InvoiceLineItemDto> LineItems { get; set; } = new();
    }

    /// <summary>
    /// GAP-11 Fix: 1 dòng chi tiết trong hóa đơn (base fee, block fee, extension fee, penalty).
    /// </summary>
    public class InvoiceLineItemDto
    {
        /// <summary>Loại: BaseHourly | BlockTier | ExtensionFee | Penalty | DepositApplied.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Mô tả hiển thị cho user (ví dụ: "Giờ đầu tiên", "Block 30 ph tiếp theo").</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Số phút áp dụng (optional, vd: 60 cho base hourly).</summary>
        public int? Minutes { get; set; }

        /// <summary>Đơn giá VND/phút (optional, vd: 1000).</summary>
        public decimal? RatePerMinute { get; set; }

        /// <summary>Số tiền (VND). Có thể âm (vd: deposit applied).</summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// GAP-8 Fix: Response DTO cho lịch sử phiên đã chơi.
    /// GET /api/v1/sessions/me/history
    /// </summary>
    public class SessionHistoryResponseDto
    {
        public Guid SessionId { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public Guid CafeId { get; set; }

        // GAP-11 Fix: LobbyId để client phân biệt lobby session vs walk-in
        public Guid? LobbyId { get; set; }

        public string GameName { get; set; } = string.Empty;

        /// <summary>Trạng thái phiên nhóm (Paid, Cancelled, v.v.).</summary>
        public GroupSessionStatus? SessionStatus { get; set; }
        public DateTime JoinedAt { get; set; }

        /// <summary>GAP-3 Fix: JoinedAt kèm timezone offset.</summary>
        public DateTimeOffset JoinedAtOffset { get; set; }

        public DateTime? PaidAt { get; set; }

        /// <summary>GAP-3 Fix: PaidAt kèm timezone offset.</summary>
        public DateTimeOffset? PaidAtOffset { get; set; }

        public int TotalMinutesPlayed { get; set; }

        // GAP-12 Fix: Đổi tên — đây là số tiền PHẢI TRẢ, không phải đã thanh toán
        public decimal TotalAmountDue { get; set; }

        // GAP-22 Fix: MemberStatus để client biết player đã về sớm / no-show
        public IndividualSessionStatus MemberStatus { get; set; }

        public string Currency { get; set; } = "VND";
    }
}
